# Install script for directory: D:/WorkingEnv/Omniconnect/Desktop-Application/native/build/win-x64/_deps/llama_cpp-src/ggml

# Set the install prefix
if(NOT DEFINED CMAKE_INSTALL_PREFIX)
  set(CMAKE_INSTALL_PREFIX "C:/Program Files (x86)/omni_llama")
endif()
string(REGEX REPLACE "/$" "" CMAKE_INSTALL_PREFIX "${CMAKE_INSTALL_PREFIX}")

# Set the install configuration name.
if(NOT DEFINED CMAKE_INSTALL_CONFIG_NAME)
  if(BUILD_TYPE)
    string(REGEX REPLACE "^[^A-Za-z0-9_]+" ""
           CMAKE_INSTALL_CONFIG_NAME "${BUILD_TYPE}")
  else()
    set(CMAKE_INSTALL_CONFIG_NAME "Release")
  endif()
  message(STATUS "Install configuration: \"${CMAKE_INSTALL_CONFIG_NAME}\"")
endif()

# Set the component getting installed.
if(NOT CMAKE_INSTALL_COMPONENT)
  if(COMPONENT)
    message(STATUS "Install component: \"${COMPONENT}\"")
    set(CMAKE_INSTALL_COMPONENT "${COMPONENT}")
  else()
    set(CMAKE_INSTALL_COMPONENT)
  endif()
endif()

# Is this installation the result of a crosscompile?
if(NOT DEFINED CMAKE_CROSSCOMPILING)
  set(CMAKE_CROSSCOMPILING "FALSE")
endif()

if(NOT CMAKE_INSTALL_LOCAL_ONLY)
  # Include the install script for the subdirectory.
  include("D:/WorkingEnv/Omniconnect/Desktop-Application/native/build/win-x64/_deps/llama_cpp-build/ggml/src/cmake_install.cmake")
endif()

if("x${CMAKE_INSTALL_COMPONENT}x" STREQUAL "xUnspecifiedx" OR NOT CMAKE_INSTALL_COMPONENT)
  file(INSTALL DESTINATION "${CMAKE_INSTALL_PREFIX}/lib" TYPE STATIC_LIBRARY FILES "D:/WorkingEnv/Omniconnect/Desktop-Application/native/build/win-x64/_deps/llama_cpp-build/ggml/src/ggml.lib")
endif()

if("x${CMAKE_INSTALL_COMPONENT}x" STREQUAL "xUnspecifiedx" OR NOT CMAKE_INSTALL_COMPONENT)
  file(INSTALL DESTINATION "${CMAKE_INSTALL_PREFIX}/include" TYPE FILE FILES
    "D:/WorkingEnv/Omniconnect/Desktop-Application/native/build/win-x64/_deps/llama_cpp-src/ggml/include/ggml.h"
    "D:/WorkingEnv/Omniconnect/Desktop-Application/native/build/win-x64/_deps/llama_cpp-src/ggml/include/ggml-cpu.h"
    "D:/WorkingEnv/Omniconnect/Desktop-Application/native/build/win-x64/_deps/llama_cpp-src/ggml/include/ggml-alloc.h"
    "D:/WorkingEnv/Omniconnect/Desktop-Application/native/build/win-x64/_deps/llama_cpp-src/ggml/include/ggml-backend.h"
    "D:/WorkingEnv/Omniconnect/Desktop-Application/native/build/win-x64/_deps/llama_cpp-src/ggml/include/ggml-blas.h"
    "D:/WorkingEnv/Omniconnect/Desktop-Application/native/build/win-x64/_deps/llama_cpp-src/ggml/include/ggml-cann.h"
    "D:/WorkingEnv/Omniconnect/Desktop-Application/native/build/win-x64/_deps/llama_cpp-src/ggml/include/ggml-cpp.h"
    "D:/WorkingEnv/Omniconnect/Desktop-Application/native/build/win-x64/_deps/llama_cpp-src/ggml/include/ggml-cuda.h"
    "D:/WorkingEnv/Omniconnect/Desktop-Application/native/build/win-x64/_deps/llama_cpp-src/ggml/include/ggml-kompute.h"
    "D:/WorkingEnv/Omniconnect/Desktop-Application/native/build/win-x64/_deps/llama_cpp-src/ggml/include/ggml-opt.h"
    "D:/WorkingEnv/Omniconnect/Desktop-Application/native/build/win-x64/_deps/llama_cpp-src/ggml/include/ggml-metal.h"
    "D:/WorkingEnv/Omniconnect/Desktop-Application/native/build/win-x64/_deps/llama_cpp-src/ggml/include/ggml-rpc.h"
    "D:/WorkingEnv/Omniconnect/Desktop-Application/native/build/win-x64/_deps/llama_cpp-src/ggml/include/ggml-sycl.h"
    "D:/WorkingEnv/Omniconnect/Desktop-Application/native/build/win-x64/_deps/llama_cpp-src/ggml/include/ggml-vulkan.h"
    "D:/WorkingEnv/Omniconnect/Desktop-Application/native/build/win-x64/_deps/llama_cpp-src/ggml/include/gguf.h"
    )
endif()

if("x${CMAKE_INSTALL_COMPONENT}x" STREQUAL "xUnspecifiedx" OR NOT CMAKE_INSTALL_COMPONENT)
  file(INSTALL DESTINATION "${CMAKE_INSTALL_PREFIX}/lib" TYPE STATIC_LIBRARY FILES "D:/WorkingEnv/Omniconnect/Desktop-Application/native/build/win-x64/_deps/llama_cpp-build/ggml/src/ggml-base.lib")
endif()

if("x${CMAKE_INSTALL_COMPONENT}x" STREQUAL "xUnspecifiedx" OR NOT CMAKE_INSTALL_COMPONENT)
  file(INSTALL DESTINATION "${CMAKE_INSTALL_PREFIX}/lib/cmake/ggml" TYPE FILE FILES
    "D:/WorkingEnv/Omniconnect/Desktop-Application/native/build/win-x64/_deps/llama_cpp-build/ggml/ggml-config.cmake"
    "D:/WorkingEnv/Omniconnect/Desktop-Application/native/build/win-x64/_deps/llama_cpp-build/ggml/ggml-version.cmake"
    )
endif()

