/*
 * omni_llama — a tiny, stable C ABI over llama.cpp.
 *
 * Why a shim instead of P/Invoking llama.cpp directly?
 *   llama.cpp's `llama_model_params` / `llama_context_params` are large,
 *   version-specific structs full of function pointers. Marshalling them by
 *   hand from C# or Dart is brittle and breaks on every upstream refactor.
 *   This shim exposes the four operations OmniConnect actually needs — load,
 *   chat, embed, free — over plain pointers and C strings, so the managed
 *   side never has to know llama.cpp's internal layout.
 *
 * The same shared library is consumed by:
 *   - Desktop:  P/Invoke from OmniConnect.Backend.  Library name: omni_llama
 *   - Mobile :  dart:ffi from the Flutter app.      Library name: libomni_llama.so
 *
 * Functions are intentionally `oc_`-prefixed and use only C types so the ABI
 * is identical on MSVC and the Android NDK.
 */
#ifndef OMNI_LLAMA_H
#define OMNI_LLAMA_H

#include <stdint.h>

#ifdef _WIN32
#  define OC_API __declspec(dllexport)
#else
#  define OC_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct oc_model oc_model;

typedef struct oc_message {
    const char * role;     /* "system" | "user" | "assistant" */
    const char * content;  /* UTF-8 */
} oc_message;

/* Called for every generated token if non-NULL, in order. The return value is
 * ignored: stop generation with oc_cancel() instead, which is safe to call from
 * any thread (this is what lets Dart use a `NativeCallable.listener`, whose
 * native-side return value is undefined). `piece` is UTF-8, NOT NUL-terminated
 * as a complete string — it is one token's bytes and may split a UTF-8
 * sequence, so append the raw bytes and decode at the end. */
typedef void (*oc_token_cb)(const char * piece, int piece_len, void * user_data);

/*
 * Load a GGUF model from disk.
 * Returns NULL on failure and writes a human-readable reason into `err`.
 * `n_ctx`     <= 0 uses the shim default (4096).
 * `n_threads` <= 0 lets the shim choose a sensible default.
 * The returned handle must be released with oc_free().
 */
OC_API oc_model * oc_load(const char * gguf_path, int n_ctx, int n_threads, char * err, int err_len);

/* Release a model handle. Safe to call with NULL. */
OC_API void oc_free(oc_model * m);

/* Size of the context window the model was loaded with (tokens). */
OC_API int oc_context_size(const oc_model * m);

/*
 * Run a chat completion against an already-formatted message list.
 *
 * The shim applies the model's own chat template, so Qwen / Llama / Gemma /
 * Phi all "just work" without the caller knowing their prompt formats.
 *
 * Returns the number of bytes written into `out` (excluding the NUL), or a
 * negative value on error. `cb` may be NULL for a blocking single-shot call.
 */
OC_API int oc_chat(
    oc_model * m,
    const oc_message * messages, int n_messages,
    int max_tokens,
    float temperature,
    float top_p,
    int top_k,
    uint32_t seed,
    char * out, int out_len,
    oc_token_cb cb, void * user_data);

/*
 * Produce a mean-pooled embedding for `text`.
 * Writes `oc_embed_dim()` floats into `out`. Returns the number of floats
 * written, or a negative value on error.
 */
OC_API int oc_embed(oc_model * m, const char * text, float * out, int out_dim);

/* Dimensionality of oc_embed() output, or 0 if unavailable. */
OC_API int oc_embed_dim(const oc_model * m);

/*
 * Ask an in-flight oc_chat() on this model to stop as soon as possible.
 * Safe to call from any thread. The flag is cleared automatically at the start
 * of the next oc_chat(), so no reset call is needed. `cancel` is 1 to cancel,
 * 0 to clear.
 */
OC_API void oc_cancel(oc_model * m, int cancel);

#ifdef __cplusplus
}
#endif

#endif /* OMNI_LLAMA_H */
