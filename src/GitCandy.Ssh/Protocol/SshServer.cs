using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace GitCandy.Ssh
{
    public class SshServer : IDisposable
    {
        private readonly object _lock = new object();
        private readonly List<Session> _sessions = new List<Session>();
        private readonly Dictionary<string, string> _hostKey = new Dictionary<string, string>();
        private bool _isDisposed;
        private bool _started;
        private TcpListener _listenser = null;

        public SshServer()
            : this(new StartingInfo())
        { }

        public SshServer(StartingInfo info)
        {
            Contract.Requires(info != null);

            StartingInfo = info;
        }

        public StartingInfo StartingInfo { get; private set; }

        public event EventHandler<Session> ConnectionAccepted;
        public event EventHandler<Exception> ExceptionRasied;

        public void Start()
        {
            lock (_lock)
            {
                CheckDisposed();
                if (_started)
                    throw new InvalidOperationException("The server is already started.");

                _listenser = StartingInfo.LocalAddress == IPAddress.IPv6Any
                    ? TcpListener.Create(StartingInfo.Port) // dual stack
                    : new TcpListener(StartingInfo.LocalAddress, StartingInfo.Port);
                _listenser.ExclusiveAddressUse = true;
                _listenser.Start();
                BeginAcceptSocket();

                _started = true;
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (_isDisposed)
                    return;
                if (!_started)
                    return;

                _listenser.Stop();

                _started = false;

                foreach (var session in _sessions.ToArray())
                {
                    try
                    {
                        session.Disconnect();
                    }
                    catch
                    {
                    }
                }
            }
        }

        public void AddHostKey(string type, string xml)
        {
            Contract.Requires(type != null);
            Contract.Requires(xml != null);

            if (!_hostKey.ContainsKey(type))
                _hostKey.Add(type, xml);
        }

        private void BeginAcceptSocket()
        {
            try
            {
                _listenser.BeginAcceptSocket(AcceptSocket, null);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch
            {
                if (_started)
                    BeginAcceptSocket();
            }
        }

        private void AcceptSocket(IAsyncResult ar)
        {
            try
            {
                var socket = _listenser.EndAcceptSocket(ar);
                Task.Run(() =>
                {
                    var session = new Session(socket, _hostKey);
                    session.Disconnected += (ss, ee) => { lock (_lock) _sessions.Remove(session); };
                    lock (_lock)
                        _sessions.Add(session);
                    try
                    {
                        if (ConnectionAccepted != null)
                            ConnectionAccepted(this, session);
                        session.EstablishConnection();
                    }
                    catch (ThreadAbortException)
                    {
                        Stop();
                    }
                    catch (SshConnectionException ex)
                    {
                        session.Disconnect(ex.DisconnectReason, ex.Message);
                        if (ExceptionRasied != null)
                            ExceptionRasied(this, ex);
                    }
                    catch (Exception ex)
                    {
                        session.Disconnect();
                        if (ExceptionRasied != null)
                            ExceptionRasied(this, ex);
                    }
                });
            }
            catch
            {
            }
            finally
            {
                BeginAcceptSocket();
            }
        }

        private void CheckDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(GetType().FullName);
        }

        #region IDisposable
        public void Dispose()
        {
            lock (_lock)
            {
                if (_isDisposed)
                    return;
                Stop();
                _isDisposed = true;
            }
        }
        #endregion
    }
}
