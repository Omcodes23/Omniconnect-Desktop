# `native/omni_llama` — OmniConnect's embedded llama.cpp runtime

OmniConnect runs its AI **inside the app process**. There is no Ollama, no
`llama-server`, no sidecar executable, no daemon, and no local HTTP service. The
only thing that ever leaves the machine is the one-time model download (plain
HTTPS from Hugging Face) — and that is only the weights file, fetched by the
app itself.

```
omni_llama.h   stable C ABI (the only thing the managed side knows about)
omni_llama.cpp llama.cpp glue: load / chat / embed / free / cancel
CMakeLists.txt fetches a pinned llama.cpp and builds the shared library
build-windows.ps1  builds + deploys omni_llama.dll for the desktop backend
```

The same `omni_llama.cpp` is compiled twice:

| Platform | Output | Consumed by | Mechanism |
|----------|--------|-------------|-----------|
| Windows  | `omni_llama.dll` | `OmniConnect.Backend` (Avalonia desktop) | P/Invoke |
| Android  | `libomni_llama.so` | Flutter app | `dart:ffi` |

Because both platforms compile the *same* translation unit, the prompt format,
sampling and truncation behaviour are identical on desktop and mobile.

---

## Why a shim instead of binding llama.cpp directly?

llama.cpp's `llama_model_params` and `llama_context_params` are large,
version-specific structs that contain function pointers and grow/shift between
releases. Marshalling them by hand from C# or Dart is fragile and breaks on
every upstream refactor. The shim exposes the six operations OmniConnect needs
over plain pointers and C strings, so the managed side never has to see
llama.cpp's internal layout.

### The ABI (`omni_llama.h`)

```
oc_model * oc_load(const char * gguf_path, int n_ctx, int n_threads, char * err, int err_len);
void       oc_free(oc_model * m);
int        oc_context_size(const oc_model * m);
int        oc_chat(oc_model * m, const oc_message * msgs, int n_msgs,
                   int max_tokens, float temperature, float top_p, int top_k,
                   uint32_t seed, char * out, int out_len,
                   oc_token_cb cb, void * user_data);
int        oc_embed(oc_model * m, const char * text, float * out, int out_dim);
int        oc_embed_dim(const oc_model * m);
void       oc_cancel(oc_model * m, int cancel);
```

`oc_chat` returns the number of bytes written into `out`, or a negative code on
error. When `cb` is non-NULL it is invoked **once per generated token** so the
UI can stream; the callback's return value is ignored deliberately — stop
generation with `oc_cancel()`, which is safe from any thread. Flutter runs
`oc_chat` on a dedicated isolate and registers a `NativeCallable.isolateLocal`
callback there: llama.cpp invokes it synchronously on the *same* thread, so no
cross-thread hop is needed. Token pieces are raw UTF-8 bytes and may split a
code point, so callers should append bytes and decode at the end.

---

## Pinned llama.cpp version

`CMakeLists.txt` fetches llama.cpp at **`b5153`** by default. The shim uses the
post-refactor C API:

```
llama_model_load_from_file   llama_init_from_model    llama_model_get_vocab
llama_get_memory             llama_memory_clear       llama_sampler_chain_*
```

so the floor is **`b4900`**. Override the tag when you want a different build:

```powershell
pwsh ./build-windows.ps1 -Tag b5153
# or, for Android, add to android/app/build.gradle.kts:
#   arguments += listOf("-DOMNI_LLAMA_CPP_TAG=b5153")
```

> **Honest caveat:** the tag above could not be executed in the environment where
> this code was authored (no MSVC / NDK toolchain available). The API surface
> used here is stable across the b49xx–b53xx range, but if a future tag renames
> or removes one of the symbols above, the build will fail loudly at compile
> time — fix by pinning to a tag that still has that symbol, or by adjusting the
> call in `omni_llama.cpp`. Nothing else in the app depends on the tag.

---

## Windows build

Build-time prerequisites (the *shipped* app has none):

* Visual Studio 2022 or 2026 with **Desktop development with C++**
* CMake 3.16+
* Git

```powershell
pwsh ./build-windows.ps1
```

The script:

1. configures with the VS generator,
2. builds `Release|x64`,
3. copies `omni_llama.dll` to `native/dist/win-x64/`, and
4. copies it into every existing backend `net10.0` output folder, so a plain
   `dotnet run` finds it without a rebuild.

`omni_llama.dll` **must sit next to `OmniConnect.Backend.dll`** at runtime. The
desktop backend looks for it on the default native search path; if it is
missing, the engine reports a clear *"Local AI runtime not installed"* error
instead of crashing.

---

## Android build

Android 10+ blocks `exec()` on files in app storage, which is exactly why the
runtime is a shared library loaded with `dlopen`/`dart:ffi` and **not** a
bundled `llama-server` binary. The Flutter app builds `libomni_llama.so`
directly through the NDK:

* `Flutter-Application/android/app/build.gradle.kts` declares
  `externalNativeBuild { cmake { path = "../../../Desktop-Application/native/omni_llama/CMakeLists.txt" } }`
* `CMakeLists.txt` detects `ANDROID` and (a) turns OpenMP **off** (the NDK ships
  no `libomp`), (b) names the output `libomni_llama.so`, and (c) keeps the C++
  STL shared via `-DANDROID_STL=c++_shared`.

Requirements: Flutter's NDK (installed automatically by the Flutter toolchain)
and CMake 3.22+ made available by the Android SDK manager. Then:

```bash
cd Flutter-Application
flutter build apk --release
# or, for a fast loop against a connected device:
flutter run
```

The first configure clones llama.cpp (a few tens of MB, cached afterwards), so
it needs network access **at build time only**.

Only `arm64-v8a` and `x86_64` are targeted — every phone and emulator that
matters for this app, at half the build time and APK overhead of including
32-bit ABIs.

---

## What this library does *not* do

* No network access, ever.
* No GPU/CUDA/Metal offload — CPU only (`n_gpu_layers = 0`), so the shipped app
  needs no driver or vendor runtime.
* No process spawning, no temp files, no `llama-server`.
