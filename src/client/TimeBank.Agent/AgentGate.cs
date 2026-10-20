namespace WillBeyond.TimeBank.Agent;

/// <summary>
/// 串行化「与服务端交互并更新状态」的操作（心跳/登录/下机/加时申请/看门狗），
/// 避免并发交错导致状态被旧响应覆盖（如登录成功后被心跳的旧会话锁回）。
/// </summary>
public class AgentGate
{
    public SemaphoreSlim Sem { get; } = new(1, 1);
}
