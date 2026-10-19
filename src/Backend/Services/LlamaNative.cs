using System.Runtime.InteropServices;

namespace OmniConnect.Backend.Services;

/// <summary>
/// Raw P/Invoke surface for the embedded llama.cpp shim built from
/// <c>native/omni_llama</c>. Everything the managed backend needs to run a
/// language model in-process goes through the small, stable ABI declared in
/// <c>omni_llama.h</c> — no server, no sidecar process, no external service.
///
/// The native library ("omni_llama") is loaded from the application output
/// folder. If it is missing, the first call throws
/// <see cref="DllNotFoundException"/>; <see cref="LocalInferenceEngine"/>
/// translates that into a clear "Local AI runtime not installed" message.
/// </summary>
internal static class LlamaNative
{
    /// <summary>
    /// Logical library name. The OS appends the platform suffix at load time
    /// (<c>omni_llama.dll</c> on Windows, <c>libomni_llama.so</c> elsewhere).
    /// </summary>
    internal const string LibraryName = "omni_llama";

    /// <summary>Mirror of the <c>oc_message</c> struct: two UTF-8 C strings.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct OcMessage
    {
        public IntPtr Role;
        public IntPtr Content;
    }

    /// <summary>
    /// Mirror of <c>oc_token_cb</c>. Called on the same thread that invoked
    /// <see cref="Chat"/>, so a plain managed delegate is safe here. The return
    /// value is ignored by the shim; cancellation goes through
    /// <see cref="Cancel"/>.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void TokenCallback(IntPtr piece, int pieceLen, IntPtr userData);

    [DllImport(LibraryName, EntryPoint = "oc_load", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr Load(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string ggufPath,
        int nCtx,
        int nThreads,
        [Out] byte[] err,
        int errLen);

    [DllImport(LibraryName, EntryPoint = "oc_free", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Free(IntPtr model);

    [DllImport(LibraryName, EntryPoint = "oc_context_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextSize(IntPtr model);

    [DllImport(LibraryName, EntryPoint = "oc_chat", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Chat(
        IntPtr model,
        [In] OcMessage[] messages,
        int nMessages,
        int maxTokens,
        float temperature,
        float topP,
        int topK,
        uint seed,
        [Out] byte[] outBuf,
        int outLen,
        TokenCallback? cb,
        IntPtr userData);

    [DllImport(LibraryName, EntryPoint = "oc_embed", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Embed(
        IntPtr model,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string text,
        [Out] float[] outBuf,
        int outDim);

    [DllImport(LibraryName, EntryPoint = "oc_embed_dim", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int EmbedDim(IntPtr model);

    [DllImport(LibraryName, EntryPoint = "oc_cancel", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Cancel(IntPtr model, int cancel);
}
