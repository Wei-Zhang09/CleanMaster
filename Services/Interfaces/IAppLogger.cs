namespace CleanMaster.Services;

/// <summary>
/// 应用日志抽象。服务层通过构造函数注入该接口，避免反向依赖 UI 层的
/// <see cref="CleanMaster.App"/> 静态方法；测试可注入空实现。
/// </summary>
public interface IAppLogger
{
    void Info(string message);
    void Error(string context, Exception? ex);
    void Diagnostic(string message);
}
