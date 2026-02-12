using System.Threading.Tasks;

namespace WgServerforWindows.Services.Interfaces
{
    public interface IDdnsProvider
    {
        string ProviderName { get; }
        Task<bool> UpdateRecordAsync(string domain, string ipAddress);
    }
}
