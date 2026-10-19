# omni_llama — OmniConnect's embedded llama.cpp runtime.
#
# This builds a single shared library (`omni_llama`) that statically links the
# pinned llama.cpp + ggml. The shipped OmniConnect app therefore has NO external
# runtime dependency: no Ollama, no `llama-server`, no daemon of any kind.
#
#   Windows : produces omni_llama.dll  (loaded by the Avalonia backend)
#   Android : produces libomni_llama.so (loaded by the Flutter app via dart:ffi)
#
# llama.cpp is fetched at a PINNED release tag so builds are reproducible.
# See native/README.md for the version policy and per-platform instructions.

cmake_minimum_required(VERSION 3.16)
project(omni_llama CXX)

if(NOT CMAKE_BUILD_TYPE AND NOT CMAKE_CONFIGURATION_TYPES)
    set(CMAKE_BUILD_TYPE Release CACHE STRING "" FORCE)
endif()

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# ── Pinned llama.cpp ────────────────────────────────────────────────────
# Requires a release that ships the modern C API used by omni_llama.cpp:
#   llama_model_load_from_file / llama_init_from_model / llama_model_get_vocab
#   llama_kv_self_clear / llama_sampler_chain_*
# That means b4900 or newer. Override with -DOMNI_LLAMA_CPP_TAG=<tag>.
set(OMNI_LLAMA_CPP_TAG "b5153" CACHE STRING "llama.cpp release tag to build against")

include(FetchContent)

set(LLAMA_BUILD_TESTS      OFF CACHE BOOL "" FORCE)
set(LLAMA_BUILD_EXAMPLES   OFF CACHE BOOL "" FORCE)
set(LLAMA_BUILD_TOOLS      OFF CACHE BOOL "" FORCE)
set(LLAMA_BUILD_SERVER     OFF CACHE BOOL "" FORCE)
set(LLAMA_BUILD_COMMON     OFF CACHE BOOL "" FORCE)
set(BUILD_SHARED_LIBS      OFF CACHE BOOL "" FORCE)   # link llama/ggml statically
set(GGML_CCACHE            OFF CACHE BOOL "" FORCE)

# The Android NDK ships no libomp; forcing OpenMP on would fail the link.
if(ANDROID)
    set(GGML_OPENMP OFF CACHE BOOL "" FORCE)
else()
    set(GGML_OPENMP ON  CACHE BOOL "" FORCE)
endif()

# llama.cpp's llamafile SGEMM kernels are enabled for every ARM target, but their
# FP16 NEON loads (vld1q_f16 / vld1_f16) only exist on AArch64. 32-bit ARM fails
# with "use of undeclared identifier 'vld1q_f16'". Disable those kernels there so
# an armeabi-v7a build still compiles (the app itself ships 64-bit ABIs only).
if(ANDROID AND (ANDROID_ABI STREQUAL "armeabi-v7a" OR ANDROID_ABI STREQUAL "armeabi-v7a-with-neon"))
    set(GGML_LLAMAFILE OFF CACHE BOOL "" FORCE)
endif()

FetchContent_Declare(
    llama_cpp
    GIT_REPOSITORY https://github.com/ggml-org/llama.cpp.git
    GIT_TAG        ${OMNI_LLAMA_CPP_TAG}
    GIT_SHALLOW    TRUE
)
FetchContent_MakeAvailable(llama_cpp)

# ── The shim ────────────────────────────────────────────────────────────
add_library(omni_llama SHARED omni_llama.cpp)

target_include_directories(omni_llama PRIVATE ${llama_cpp_SOURCE_DIR}/include)
target_link_libraries(omni_llama PRIVATE llama ggml)

set_target_properties(omni_llama PROPERTIES
    OUTPUT_NAME "omni_llama"
    CXX_VISIBILITY_PRESET hidden
    VISIBILITY_INLINES_HIDDEN ON
    PREFIX ""                     # Windows wants omni_llama.dll, not lib…dll
)

if(ANDROID)
    # Android needs the .so named libomni_llama.so for dart:ffi's default lookup.
    set_target_properties(omni_llama PROPERTIES PREFIX "lib")
endif()

# Keep the OpenMP runtime out of the public ABI surface.
if(NOT MSVC)
    target_link_options(omni_llama PRIVATE -Wl,--exclude-libs,ALL)
endif()
