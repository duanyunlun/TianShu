using Windows.Agent.Uia;

namespace Windows.Agent.Interface;

public interface IUiaService
{
    Task<string> GetTreeAsync(string windowTitleRegex, int depth = 3, string backend = "uia3");
    Task<string> FindAsync(string windowTitleRegex, UiaSelector selector, int limit = 5, string backend = "uia3");
    Task<string> InvokeAsync(string windowTitleRegex, UiaSelector selector, string backend = "uia3");
    Task<string> SetValueAsync(string windowTitleRegex, UiaSelector selector, string value, string backend = "uia3");
}
