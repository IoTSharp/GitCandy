using System.Text;

namespace GitCandy.Ssh.Messages.Connection
{
    public class EnvironmentRequestMessage : ChannelRequestMessage
    {
        public string VariableName { get; private set; }

        public string VariableValue { get; private set; }

        protected override void OnLoad(SshDataWorker reader)
        {
            base.OnLoad(reader);

            VariableName = reader.ReadString(Encoding.ASCII);
            VariableValue = reader.ReadString(Encoding.ASCII);
        }
    }
}
