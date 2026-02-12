using System.Collections.Generic;
using System.Threading.Tasks;

namespace WgServerforWindows.Services.Interfaces
{
    public interface ILogService
    {
        Task<string> GetLogsAsync();
    }
}
