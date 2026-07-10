
namespace GitCandy.Ssh.Messages.Connection
{
    public class ExitStatusMessage : ChannelRequestMessage
    {
        public uint ExitStatus { get; set; }

        protected override void OnGetPacket(SshDataWorker writer)
        {
            RequestType = "exit-status";
            WantReply = false;

            base.OnGetPacket(writer);

            writer.Write(ExitStatus);
        }
    }
}
