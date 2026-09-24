// ScreenPinNotes - a desktop sticky notes app for Windows 11
// Copyright (C) 2026 umineko73
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, version 3 of the License.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ScreenPinNotes.Services;

/// <summary>
/// そのファイルを今この瞬間に開いているプロセスが、自分のほかに居るかを尋ねる。
/// ログを書くアプリはファイルを開いたまま追記するので、閉じるまで
/// FileSystemWatcher の通知が届かないことがある。開きっぱなしだと分かれば、
/// しばらく変化が無くても確認（ポーリング）を続ける判断ができる。
/// </summary>
/// <remarks>
/// Windows に「このファイルを開いているプロセスの一覧」を尋ねる
/// (NtQueryInformationFile / FileProcessIdsUsingFileInformation)。
/// 尋ねるにはファイルのハンドルが要るが、中身は読まないので FILE_READ_ATTRIBUTES
/// だけで開く。この開き方は共有の取り合いに加わらないため、こちらがハンドルを
/// 持っている間に書き手が排他（FileShare.None）で開き直しても弾かれない。
/// 逆に、排他で掴まれたままのファイルもこの開き方なら開けるので、中身を読めない
/// 相手でも誰が掴んでいるかは分かる。
/// 1回あたり数十ミリ秒かかるので、止める判断のときだけ呼ぶこと。
/// </remarks>
public static class FileHolders
{
    private const int FileProcessIdsUsingFileInformation = 47;
    private const int StatusSuccess = 0;
    private const int StatusBufferOverflow = unchecked((int)0x80000005);
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    private const int StatusBufferTooSmall = unchecked((int)0xC0000023);
    private const int SharingViolation = 32;
    private const int LockViolation = 33;
    private const uint FileReadAttributes = 0x80;
    private const uint ShareAll = 0x1 | 0x2 | 0x4;
    private const uint OpenExisting = 3;

    /// <summary>問い合わせの手応え。</summary>
    public enum HoldState
    {
        /// <summary>確かめられなかった（ファイルが無い、対応していないファイルシステムなど）。</summary>
        Unknown,
        /// <summary>自分のほかに開いているプロセスは居ない。</summary>
        NotHeld,
        /// <summary>ほかのプロセスが開いたままにしている。</summary>
        HeldByAnotherProcess,
    }

    /// <summary>
    /// 自分以外のプロセスがこのファイルを開いたままかを確かめる。
    /// 数十ミリ秒かかるので、判断が必要になったときだけ呼ぶこと。
    /// </summary>
    public static HoldState Query(string path)
    {
        try
        {
            // 属性を読むだけの開き方。誰の邪魔もしない代わりに中身も読めないが、
            // 「誰が開いているか」を尋ねるにはこれで足りる。
            using var handle = CreateFileW(path, FileReadAttributes, ShareAll, IntPtr.Zero,
                OpenExisting, 0, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                // 属性だけでも開けないほど強く掴まれているなら、掴まれているのは確か。
                // それ以外（ファイルが無い、権限が足りない）は分からないものとして扱う。
                var error = Marshal.GetLastWin32Error();
                return error is SharingViolation or LockViolation
                    ? HoldState.HeldByAnotherProcess
                    : HoldState.Unknown;
            }
            return QueryHandle(handle);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            return HoldState.Unknown;
        }
    }

    private static HoldState QueryHandle(SafeFileHandle handle)
    {
        // 先頭の ULONG が件数で、そのうしろにポインタ幅の PID が並ぶ。
        // 足りなければ倍にして尋ね直す（開いているプロセスが増えることもある）。
        var size = 1024;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var iosb = default(IoStatusBlock);
                var status = NtQueryInformationFile(handle, ref iosb, buffer, size,
                    FileProcessIdsUsingFileInformation);
                if (status is StatusBufferOverflow or StatusInfoLengthMismatch or StatusBufferTooSmall)
                {
                    size *= 4;
                    continue;
                }
                if (status != StatusSuccess)
                    return HoldState.Unknown;

                var count = Marshal.ReadInt32(buffer);
                var capacity = (size - IntPtr.Size) / IntPtr.Size;
                if (count < 0 || count > capacity)
                    return HoldState.Unknown;

                var self = Environment.ProcessId;
                for (var i = 0; i < count; i++)
                {
                    // 尋ねるのに使ったハンドル自身は並ばないが、同じプロセスの
                    // 別のハンドル（読み込み中の付箋など）は並ぶので、自分は外す。
                    var pid = Marshal.ReadIntPtr(buffer, IntPtr.Size + (i * IntPtr.Size)).ToInt64();
                    if (pid != self)
                        return HoldState.HeldByAnotherProcess;
                }
                return HoldState.NotHeld;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        return HoldState.Unknown;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock
    {
        public IntPtr Status;
        public IntPtr Information;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationFile(SafeFileHandle fileHandle, ref IoStatusBlock ioStatusBlock,
        IntPtr fileInformation, int length, int fileInformationClass);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
}
