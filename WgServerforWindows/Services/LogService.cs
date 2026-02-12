using System;
using System.IO;
using System.Threading.Tasks;
using WgAPI;
using WgServerforWindows.Services.Interfaces;

namespace WgServerforWindows.Services
{
    public class LogService : ILogService
    {
        public async Task<string> GetLogsAsync()
        {
            string tempFile = Path.GetTempFileName();
            try
            {
                var wg = new WireGuardExe();
                // wireguard.exe /dumplog /to "path"
                // WireGuardCommand adds the switch and args.
                // We need to pass "/dumplog" as switch and "/to", tempFile as args.
                // However, WireGuardCommand args is string[].
                
                var command = new WireGuardCommand("/dumplog", WhichExe.WireGuardExe, "/to", tempFile);
                
                // ExecuteCommand is synchronous in WireGuardExe (it has a Task.Run wrapper but blocks).
                // Ideally we should wrap this in Task.Run if it takes time.
                await Task.Run(() => wg.ExecuteCommand(command, out _));

                if (File.Exists(tempFile))
                {
                    return await File.ReadAllTextAsync(tempFile);
                }
            }
            catch (Exception)
            {
                // Log error or return empty
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    try
                    {
                        File.Delete(tempFile);
                    }
                    catch { }
                }
            }

            return string.Empty;
        }
    }
}
