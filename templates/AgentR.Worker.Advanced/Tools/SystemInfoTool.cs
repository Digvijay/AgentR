using System.Runtime.InteropServices;

public class SystemInfoTool
{
    public string GetInfo()
    {
        return $"OS: {RuntimeInformation.OSDescription}, Arch: {RuntimeInformation.ProcessArchitecture}, Time: {DateTime.UtcNow}";
    }
}
