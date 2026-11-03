using System.Runtime.InteropServices;

namespace TagForceSpecial.PC.Setup;

internal static class WindowsFilePicker
{
    public static string? ChooseIso()
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Source selection currently requires Windows.");
        IntPtr buffer = Marshal.AllocHGlobal(32768 * sizeof(char));
        try
        {
            Marshal.WriteInt16(buffer, 0);
            var dialog = new OpenFileName
            {
                Size = Marshal.SizeOf<OpenFileName>(), Filter = "PSP disc image (*.iso)\0*.iso\0All files\0*.*\0\0",
                File = buffer, MaximumFile = 32768, Title = "Select your original Tag Force Special English v2.1 ISO",
                Flags = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000008
            };
            if (GetOpenFileName(dialog)) return Marshal.PtrToStringUni(buffer);
            if (CommDlgExtendedError() != 0) throw new InvalidOperationException("Source selection could not open. Try selecting the ISO again.");
            return null;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class OpenFileName
    {
        public int Size;
        public IntPtr Owner, Instance;
        public string? Filter;
        public IntPtr CustomFilter;
        public int MaximumCustomFilter, FilterIndex;
        public IntPtr File;
        public int MaximumFile;
        public IntPtr FileTitle;
        public int MaximumFileTitle;
        public string? InitialDirectory, Title;
        public int Flags;
        public ushort FileOffset, ExtensionOffset;
        public string? DefaultExtension;
        public IntPtr CustomData, Hook, Template, Reserved;
        public int ReservedWord, ExtendedFlags;
    }
    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileName([In, Out] OpenFileName dialog);
    [DllImport("comdlg32.dll")] private static extern int CommDlgExtendedError();
}
