namespace CleanMaster.Services;

/// <summary>
/// 集中式错误处理工具，提供安全执行包装和异常上报。
/// </summary>
public static class ErrorHandler
{
    /// <summary>
    /// 安全执行操作，捕获异常并记录日志。
    /// 用于非关键路径（如单个文件的扫描/删除），失败不应阻断整体流程。
    /// </summary>
    /// <param name="action">要执行的操作</param>
    /// <param name="context">错误上下文标识（用于日志检索）</param>
    /// <param name="onError">可选回调，错误时通知调用方</param>
    /// <returns>操作是否成功</returns>
    public static bool SafeExecute(Action action, string context, Action<string>? onError = null)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            App.LogError(context, ex);
            onError?.Invoke(ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 安全执行带返回值的操作，失败时返回默认值。
    /// </summary>
    /// <param name="func">要执行的操作</param>
    /// <param name="context">错误上下文标识</param>
    /// <param name="defaultValue">失败时的默认返回值</param>
    /// <returns>操作结果或默认值</returns>
    public static T SafeExecute<T>(Func<T> func, string context, T defaultValue = default!)
    {
        try
        {
            return func();
        }
        catch (Exception ex)
        {
            App.LogError(context, ex);
            return defaultValue;
        }
    }

    /// <summary>
    /// 异步安全执行，失败时返回默认值。
    /// </summary>
    public static async Task<T> SafeExecuteAsync<T>(Func<Task<T>> func, string context, T defaultValue = default!)
    {
        try
        {
            return await func();
        }
        catch (Exception ex)
        {
            App.LogError(context, ex);
            return defaultValue;
        }
    }

    /// <summary>
    /// 异步安全执行无返回值操作。
    /// </summary>
    public static async Task<bool> SafeExecuteAsync(Func<Task> func, string context)
    {
        try
        {
            await func();
            return true;
        }
        catch (Exception ex)
        {
            App.LogError(context, ex);
            return false;
        }
    }
}
