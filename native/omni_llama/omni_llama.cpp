/*
 * omni_llama.cpp — implementation of the stable C ABI declared in omni_llama.h.
 *
 * Built against a PINNED llama.cpp release. See native/README.md for the exact
 * tag. The code targets the llama.cpp C API shipped by the pinned release:
 *
 *   llama_model_load_from_file / llama_init_from_model / llama_model_get_vocab
 *   llama_kv_self_clear / llama_sampler_chain_*
 *
 * NOTE: the KV-cache reset uses llama_kv_self_clear(), which is what the
 * pinned tag exposes. The newer llama_get_memory()/llama_memory_clear() pair
 * arrived after b5153; if the tag is bumped past that refactor, switch both
 * call sites back.
 *
 * Nothing here talks to the network and nothing shells out: llama.cpp runs
 * inside this shared library, inside the host process.
 */

#include "omni_llama.h"

#include "llama.h"

#include <algorithm>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

struct oc_model {
    llama_model   * model = nullptr;
    llama_context * ctx   = nullptr;   // chat / completion
    llama_context * ectx  = nullptr;   // lazy, embeddings-only
    const llama_vocab * vocab = nullptr;
    int   n_ctx     = 0;
    int   n_threads = 0;
    int   n_embd    = 0;
    std::mutex mtx;
    std::atomic<bool> cancel{false};
};

namespace {

constexpr int kDefaultContext = 4096;
constexpr int kMinCtx         = 512;
constexpr int kMaxCtx         = 32768;
constexpr int kBatchSize      = 1024;

void set_err(char * err, int err_len, const char * msg) {
    if (err && err_len > 0) {
        std::snprintf(err, static_cast<size_t>(err_len), "%s", msg ? msg : "unknown error");
    }
}

void backend_init_once() {
    static std::once_flag flag;
    std::call_once(flag, [] { llama_backend_init(); });
}

int default_threads() {
    const unsigned hc = std::thread::hardware_concurrency();
    if (hc == 0) return 2;
    // Leave headroom; more threads rarely help past ~8 on a shared machine.
    return static_cast<int>(std::min<unsigned>(hc, 8u));
}

int clamp_ctx(int n_ctx) {
    if (n_ctx <= 0) return kDefaultContext;
    return std::max(kMinCtx, std::min(n_ctx, kMaxCtx));
}

bool token_to_piece(const llama_vocab * vocab, llama_token id, std::string & out) {
    char buf[256];
    int n = llama_token_to_piece(vocab, id, buf, sizeof(buf), 0, /*special=*/true);
    if (n < 0) {
        // Piece longer than our scratch buffer — retry with the required size.
        std::vector<char> big(static_cast<size_t>(-n) + 1);
        n = llama_token_to_piece(vocab, id, big.data(), static_cast<int>(big.size()), 0, true);
        if (n < 0) return false;
        out.append(big.data(), static_cast<size_t>(n));
        return true;
    }
    out.append(buf, static_cast<size_t>(n));
    return true;
}

// Tokenize with a growable buffer. Returns false on hard failure.
bool tokenize_all(const llama_vocab * vocab, const std::string & text,
                  std::vector<llama_token> & out) {
    int n = static_cast<int>(text.size()) + 16;
    out.resize(static_cast<size_t>(n));
    int written = llama_tokenize(vocab, text.data(), static_cast<int>(text.size()),
                                 out.data(), n, /*add_special=*/true, /*parse_special=*/true);
    if (written < 0) {
        out.resize(static_cast<size_t>(-written));
        written = llama_tokenize(vocab, text.data(), static_cast<int>(text.size()),
                                 out.data(), static_cast<int>(-written), true, true);
        if (written < 0) return false;
    }
    out.resize(static_cast<size_t>(written));
    return true;
}

// Fallback prompt formatter for the rare GGUF that ships without a template.
std::string fallback_template(const oc_message * messages, int n_messages) {
    std::string s;
    for (int i = 0; i < n_messages; ++i) {
        const char * role = messages[i].role ? messages[i].role : "user";
        const char * content = messages[i].content ? messages[i].content : "";
        s += "<|im_start|>";
        s += role;
        s += "\n";
        s += content;
        s += "<|im_end|>\n";
    }
    s += "<|im_start|>assistant\n";
    return s;
}

std::string apply_chat_template(const oc_model * m, const oc_message * messages, int n_messages) {
    const char * tmpl = llama_model_chat_template(m->model, /*name=*/nullptr);
    if (!tmpl) {
        return fallback_template(messages, n_messages);
    }

    std::vector<llama_chat_message> chat;
    chat.reserve(static_cast<size_t>(n_messages));
    for (int i = 0; i < n_messages; ++i) {
        chat.push_back(llama_chat_message{
            messages[i].role ? messages[i].role : "user",
            messages[i].content ? messages[i].content : ""});
    }

    int needed = llama_chat_apply_template(tmpl, chat.data(), chat.size(),
                                           /*add_ass=*/true, nullptr, 0);
    if (needed < 0) {
        return fallback_template(messages, n_messages);
    }

    std::string buf(static_cast<size_t>(needed), '\0');
    int written = llama_chat_apply_template(tmpl, chat.data(), chat.size(), true,
                                            buf.data(), static_cast<int>(buf.size()));
    if (written < 0) {
        return fallback_template(messages, n_messages);
    }
    buf.resize(static_cast<size_t>(written));
    return buf;
}

llama_sampler * build_sampler(float temperature, float top_p, int top_k, uint32_t seed) {
    auto params = llama_sampler_chain_default_params();
    params.no_perf = true;
    llama_sampler * chain = llama_sampler_chain_init(params);

    if (temperature <= 0.0f) {
        llama_sampler_chain_add(chain, llama_sampler_init_greedy());
        return chain;
    }

    if (top_k > 0) {
        llama_sampler_chain_add(chain, llama_sampler_init_top_k(top_k));
    }
    if (top_p > 0.0f && top_p < 1.0f) {
        llama_sampler_chain_add(chain, llama_sampler_init_top_p(top_p, 1));
    }
    llama_sampler_chain_add(chain, llama_sampler_init_temp(temperature));
    llama_sampler_chain_add(chain, llama_sampler_init_dist(seed));
    return chain;
}

} // namespace

extern "C" {

OC_API oc_model * oc_load(const char * gguf_path, int n_ctx, int n_threads,
                          char * err, int err_len) {
    if (!gguf_path || !*gguf_path) {
        set_err(err, err_len, "empty model path");
        return nullptr;
    }

    backend_init_once();

    auto * m = new (std::nothrow) oc_model();
    if (!m) {
        set_err(err, err_len, "out of memory");
        return nullptr;
    }

    m->n_ctx = clamp_ctx(n_ctx);
    m->n_threads = n_threads > 0 ? n_threads : default_threads();

    llama_model_params mparams = llama_model_default_params();
    mparams.n_gpu_layers = 0;   // CPU-only keeps the shipped app dependency-free.

    m->model = llama_model_load_from_file(gguf_path, mparams);
    if (!m->model) {
        set_err(err, err_len, "failed to load GGUF model (corrupt, unsupported, or out of memory)");
        delete m;
        return nullptr;
    }

    m->vocab = llama_model_get_vocab(m->model);
    m->n_embd = static_cast<int>(llama_model_n_embd(m->model));

    llama_context_params cparams = llama_context_default_params();
    cparams.n_ctx           = static_cast<uint32_t>(m->n_ctx);
    cparams.n_batch         = static_cast<uint32_t>(std::min(kBatchSize, m->n_ctx));
    cparams.n_ubatch        = static_cast<uint32_t>(std::min(kBatchSize, m->n_ctx));
    cparams.n_threads       = m->n_threads;
    cparams.n_threads_batch = m->n_threads;

    m->ctx = llama_init_from_model(m->model, cparams);
    if (!m->ctx) {
        set_err(err, err_len, "failed to create inference context");
        llama_model_free(m->model);
        delete m;
        return nullptr;
    }

    return m;
}

OC_API void oc_free(oc_model * m) {
    if (!m) return;
    if (m->ectx)  llama_free(m->ectx);
    if (m->ctx)   llama_free(m->ctx);
    if (m->model) llama_model_free(m->model);
    delete m;
}

OC_API int oc_context_size(const oc_model * m) {
    return m ? m->n_ctx : 0;
}

OC_API int oc_chat(oc_model * m,
                   const oc_message * messages, int n_messages,
                   int max_tokens,
                   float temperature, float top_p, int top_k, uint32_t seed,
                   char * out, int out_len,
                   oc_token_cb cb, void * user_data) {
    if (!m || !m->ctx || !messages || n_messages <= 0) return -1;
    if (!out || out_len <= 0) return -1;
    if (max_tokens <= 0) max_tokens = 512;

    std::lock_guard<std::mutex> lock(m->mtx);

    const std::string prompt = apply_chat_template(m, messages, n_messages);

    std::vector<llama_token> tokens;
    if (!tokenize_all(m->vocab, prompt, tokens)) {
        return -2;
    }

    // Keep room for the reply: drop the oldest tokens if the prompt is too big.
    const size_t budget = static_cast<size_t>(std::max(kMinCtx, m->n_ctx - max_tokens - 8));
    if (tokens.size() > budget) {
        tokens.erase(tokens.begin(), tokens.begin() + (tokens.size() - budget));
    }

    llama_kv_self_clear(m->ctx);

    // A stale cancel from a previous generation must not kill this one.
    m->cancel.store(false, std::memory_order_release);

    const int batch = static_cast<int>(std::min(kBatchSize, m->n_ctx));
    for (size_t i = 0; i < tokens.size(); i += static_cast<size_t>(batch)) {
        const int n = static_cast<int>(std::min<size_t>(static_cast<size_t>(batch), tokens.size() - i));
        llama_batch b = llama_batch_get_one(tokens.data() + i, n);
        if (llama_decode(m->ctx, b) != 0) return -3;
    }

    llama_sampler * smpl = build_sampler(temperature, top_p, top_k, seed);

    std::string result;
    result.reserve(1024);

    for (int i = 0; i < max_tokens; ++i) {
        if (m->cancel.load(std::memory_order_acquire)) break;

        llama_token id = llama_sampler_sample(smpl, m->ctx, -1);
        if (llama_vocab_is_eog(m->vocab, id)) break;
        llama_sampler_accept(smpl, id);

        std::string piece;
        if (token_to_piece(m->vocab, id, piece)) {
            if (cb) cb(piece.data(), static_cast<int>(piece.size()), user_data);
            result += piece;
        }

        llama_batch nb = llama_batch_get_one(&id, 1);
        if (llama_decode(m->ctx, nb) != 0) break;
    }

    llama_sampler_free(smpl);

    const int to_copy = std::min<int>(out_len - 1, static_cast<int>(result.size()));
    if (to_copy > 0) std::memcpy(out, result.data(), static_cast<size_t>(to_copy));
    out[to_copy] = '\0';
    return to_copy;
}

OC_API int oc_embed(oc_model * m, const char * text, float * out, int out_dim) {
    if (!m || !text || !out) return -1;
    if (m->n_embd <= 0) return -2;

    std::lock_guard<std::mutex> lock(m->mtx);

    if (!m->ectx) {
        llama_context_params cparams = llama_context_default_params();
        cparams.n_ctx         = static_cast<uint32_t>(std::min(m->n_ctx, 2048));
        cparams.n_batch       = std::min<uint32_t>(static_cast<uint32_t>(kBatchSize), cparams.n_ctx);
        cparams.n_ubatch      = cparams.n_batch;
        cparams.n_threads     = m->n_threads;
        cparams.n_threads_batch = m->n_threads;
        cparams.embeddings    = true;
        cparams.pooling_type  = LLAMA_POOLING_TYPE_MEAN;

        m->ectx = llama_init_from_model(m->model, cparams);
        if (!m->ectx) return -3;
    }

    std::vector<llama_token> tokens;
    if (!tokenize_all(m->vocab, std::string(text), tokens)) return -4;
    if (tokens.empty()) return -5;

    llama_kv_self_clear(m->ectx);

    const int batch = static_cast<int>(std::min(kBatchSize, m->n_ctx));
    for (size_t i = 0; i < tokens.size(); i += static_cast<size_t>(batch)) {
        const int n = static_cast<int>(std::min<size_t>(static_cast<size_t>(batch), tokens.size() - i));
        llama_batch b = llama_batch_get_one(tokens.data() + i, n);
        if (llama_decode(m->ectx, b) != 0) return -6;
    }

    const float * emb = llama_get_embeddings_seq(m->ectx, 0);
    if (!emb) emb = llama_get_embeddings_ith(m->ectx, -1);
    if (!emb) return -7;

    const int n = std::min(out_dim, m->n_embd);
    std::memcpy(out, emb, static_cast<size_t>(n) * sizeof(float));
    return n;
}

OC_API int oc_embed_dim(const oc_model * m) {
    return m ? m->n_embd : 0;
}

OC_API void oc_cancel(oc_model * m, int cancel) {
    if (m) m->cancel.store(cancel != 0, std::memory_order_release);
}

} // extern "C"
