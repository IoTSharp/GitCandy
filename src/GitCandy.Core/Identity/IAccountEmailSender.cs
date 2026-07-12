namespace GitCandy.Identity;

/// <summary>投递 Identity 账号确认与恢复邮件。</summary>
public interface IAccountEmailSender
{
    /// <summary>发送一次性账号操作链接。</summary>
    /// <param name="recipient">目标邮箱。</param>
    /// <param name="subject">邮件主题。</param>
    /// <param name="actionLink">一次性 HTTPS 操作链接。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>邮件是否已交给投递端。</returns>
    Task<bool> SendActionLinkAsync(
        string recipient,
        string subject,
        Uri actionLink,
        CancellationToken cancellationToken = default);
}
