using System.Runtime.InteropServices;
using System.Text;

namespace LibLouis.NET.Test;

/// <summary>
/// Raw P/Invoke used to characterise native behaviour without going through the wrapper.
/// </summary>
/// <remarks>
/// Strings are passed as pre-encoded NUL terminated UTF-8 rather than as managed strings, so
/// there is no marshalling behaviour of our own between the test and liblouis.
/// </remarks>
internal static class SafeNativeMethods
{
    /// <summary>
    /// Bytes per liblouis widechar: 2 for a UCS-2 build, 4 for UCS-4.
    /// </summary>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [DllImport("liblouis", EntryPoint = "lou_charSize")]
    internal static extern int lou_charSize();

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [DllImport("liblouis", EntryPoint = "lou_translateString")]
    internal static extern int lou_translateString(
        byte[] tableList,
        byte[] inbuf,
        ref int inlen,
        byte[] outbuf,
        ref int outlen,
        ushort[]? typeform,
        byte[]? spacing,
        int mode);

    /// <summary>
    /// lou_translate with the display table compiled apart from the translation table, which is
    /// how lou_checkyaml runs the braille specs (tools/brl_checks.c). The public API compiles one
    /// table list for both, so a display list that names a translation table leaks that table's
    /// rules into the translation. Internal, but exported on every platform this repository builds.
    /// </summary>
    /// <remarks>
    /// The buffers are uint[] because widechar is 4 bytes in the --enable-ucs4 build the runtime
    /// packages ship. Positions, cursor, typeform, spacing and the rule trace are not needed by
    /// the specs this is used for, and are passed as null.
    /// </remarks>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [DllImport("liblouis", EntryPoint = "_lou_translate")]
    internal static extern int _lou_translate(
        byte[] tableList,
        byte[] displayTableList,
        uint[] inbuf,
        ref int inlen,
        uint[] outbuf,
        ref int outlen,
        nint typeform,
        nint spacing,
        nint outputPos,
        nint inputPos,
        nint cursorPos,
        int mode,
        nint rules,
        nint rulesLen);

    /// <summary>
    /// The back translation counterpart of <see cref="_lou_translate"/>.
    /// </summary>
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [DllImport("liblouis", EntryPoint = "_lou_backTranslate")]
    internal static extern int _lou_backTranslate(
        byte[] tableList,
        byte[] displayTableList,
        uint[] inbuf,
        ref int inlen,
        uint[] outbuf,
        ref int outlen,
        nint typeform,
        nint spacing,
        nint outputPos,
        nint inputPos,
        nint cursorPos,
        int mode,
        nint rules,
        nint rulesLen);

    /// <summary>
    /// Encodes a string the way liblouis expects a <c>const char *</c>.
    /// </summary>
    internal static byte[] Utf8(string value)
    {
        return Encoding.UTF8.GetBytes(value + "\0");
    }
}
