using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Server
{
    public partial class Server : Form
    {
        string IP_Adr;

        private bool active = false;
        private Thread listener = null;
        private long id = 1;

        string Data;

        readonly SerialPort serialPort = new SerialPort();


        private struct MyClient
        {
            public long id;
            public StringBuilder username;
            public TcpClient client;
            public NetworkStream stream;
            public byte[] buffer;
            public StringBuilder data;
            public EventWaitHandle handle;
        };
        private ConcurrentDictionary<long, MyClient> clients = new ConcurrentDictionary<long, MyClient>();
        private Task send = null;
        private Thread disconnect = null;
        private bool exit = false;


        public Server()
        {
            InitializeComponent();

            Control.CheckForIllegalCrossThreadCalls = false;
            serialPort.DataReceived += new SerialDataReceivedEventHandler(DataReceived);

            IPAddress ip = Dns.GetHostByName(Dns.GetHostName()).AddressList[0];
            IP_Adr = ip.ToString();

            Label_Logo_Connect_lb.ForeColor = System.Drawing.Color.Red;

            label_status_server.Visible = false;

            label_ip_adr.Text = "IP адрес этого компьютера: " + IP_Adr;
           

            /*- - - - - -Установка параметра интерфейса серийного порта------*/

            //Проверьте, содержит ли он последовательный порт
            string[] str = SerialPort.GetPortNames();
            if (str == null)
            {
                MessageBox.Show("Эта машина не имеет серийный порт！", " Error ");
                return;
            }
            // Добавляем последовательный порт
            foreach (string s in str)
            {
                comboBoxCom.Items.Add(s);
            }
            // Установка параметров последовательного порта по умолчанию
            comboBoxCom.SelectedIndex = 0;

            /*- - - - - -Настройка скорости передачи данных в бодах------ - */
            string[] baudRate = { "9600", "19200", "38400", "57600", "115200" };
            foreach (string s in baudRate)
            {
                comboBoxBaudRate.Items.Add(s);
            }
            comboBoxBaudRate.SelectedIndex = 0;

            /*- - - - - -Установка битов данных-------*/
            string[] dataBit = { "5", "6", "7", "8" };
            foreach (string s in dataBit)
            {
                comboBoxDataBit.Items.Add(s);
            }
            comboBoxDataBit.SelectedIndex = 3;

            /*- - - - - -Установка контрольного бита-------*/
            string[] checkBit = { "None", "Even", "Odd", "Mask", "Space" };
            foreach (string s in checkBit)
            {
                comboBoxCheckBit.Items.Add(s);
            }
            comboBoxCheckBit.SelectedIndex = 0;

            /*- - - - - -Установка стоп - бита------ - */
            string[] stopBit = { "1", "1.5", "2" };
            foreach (string s in stopBit)
            {
                comboBoxStopBit.Items.Add(s);
            }
            comboBoxStopBit.SelectedIndex = 0;

            this.StartPosition = FormStartPosition.WindowsDefaultLocation;
        }
        

    

        private void Log(string msg = "") // clear the log if message is not supplied or is empty
        {
            if (!exit)
            {
                logTextBox.Invoke((MethodInvoker)delegate
                {
                    if (msg.Length > 0)
                    {
                        logTextBox.AppendText(string.Format("[ {0} ] {1}{2}", DateTime.Now.ToString("HH:mm"), msg, Environment.NewLine));
                    }
                    else
                    {
                        logTextBox.Clear();
                    }
                });
            }
        }
        private string ErrorMsg(string msg)
        {
            return string.Format("ERROR: {0}", msg);
        }

        private string SystemMsg(string msg)
        {
            return string.Format("SYSTEM: {0}", msg);
        }

        private void Active(bool status)
        {
            if (!exit)
            {
                buttonOpenCloseCom.Invoke((MethodInvoker)delegate
                {
                    active = status;
                    if (status)
                    {
                        label_status_server.Visible = true;
                        portTextBox.Enabled = false;
                        buttonOpenCloseCom.Text = "Стоп";
                        Log(SystemMsg("Сервер запущен"));
                        Label_Logo_Connect_lb.Text = "Server Available";
                        Label_Logo_Connect_lb.ForeColor = System.Drawing.Color.DarkGreen;
                    }
                    else
                    {
                        label_status_server.Visible = false;
                        portTextBox.Enabled = true;
                        buttonOpenCloseCom.Text = "Старт";
                        Log(SystemMsg("Сервер остановлен"));
                        Label_Logo_Connect_lb.Text = "Server Disabled";
                        Label_Logo_Connect_lb.ForeColor = System.Drawing.Color.Red;
                    }
                });
            }
        }

        private void AddToGrid(long id, string name)
        {
            if (!exit)
            {
                clientsDataGridView.Invoke((MethodInvoker)delegate
                {
                    string[] row = new string[] { id.ToString(), name };
                    clientsDataGridView.Rows.Add(row);
                    totalLabel.Text = string.Format("Подключено: {0}", clientsDataGridView.Rows.Count);
                });
            }
        }

        private void RemoveFromGrid(long id)
        {
            if (!exit)
            {
                clientsDataGridView.Invoke((MethodInvoker)delegate
                {
                    foreach (DataGridViewRow row in clientsDataGridView.Rows)
                    {
                        if (row.Cells["identifier"].Value.ToString() == id.ToString())
                        {
                            clientsDataGridView.Rows.RemoveAt(row.Index);
                            break;
                        }
                    }
                    totalLabel.Text = string.Format("Всего клиентов: {0}", clientsDataGridView.Rows.Count);
                });
            }
        }

        private void Read(IAsyncResult result)
        {
            MyClient obj = (MyClient)result.AsyncState;
            int bytes = 0;
            if (obj.client.Connected)
            {
                try
                { 
                    bytes = obj.stream.EndRead(result);
                }
                catch (Exception ex)
                {
                    Log(ErrorMsg(ex.Message));
                }
            }
            if (bytes > 0)
            {
                obj.data.AppendFormat("{0}", Encoding.GetEncoding(1251).GetString(obj.buffer, 0, bytes));
                try
                {
                    if (obj.stream.DataAvailable)
                    {
                        obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(Read), obj);
                    }
                    else
                    {
                        string name = Convert.ToString(((System.Net.IPEndPoint)obj.client.Client.RemoteEndPoint).Address).ToString();
                        string msg = string.Format("{0}: {1}", name, obj.data + "\n");
                        
                        Log(msg);
                        Send(msg, obj.id);
                        obj.data.Clear();
                        obj.handle.Set();
                    }
                }
                catch (Exception ex)
                {
                    obj.data.Clear();
                    Log(ErrorMsg(ex.Message));
                    obj.handle.Set();
                }
            }
            else
            {
                obj.client.Close();
                obj.handle.Set();
            }
        }

        private void ReadAuth(IAsyncResult result)
        {
            MyClient obj = (MyClient)result.AsyncState;
            int bytes = 0;
            if (obj.client.Connected)
            {
                try
                {
                    bytes = obj.stream.EndRead(result);
                }
                catch (Exception ex)
                {
                    Log(ErrorMsg(ex.Message));
                }
            }
            if (bytes > 0)
            {
                obj.data.AppendFormat("{0}", Encoding.GetEncoding(1251).GetString(obj.buffer, 0, bytes));
                try
                {
                    if (obj.stream.DataAvailable)
                    {
                        obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(ReadAuth), obj);
                    }
                    else
                    {
                        JavaScriptSerializer json = new JavaScriptSerializer(); // feel free to use JSON serializer
                        Dictionary<string, string> data = json.Deserialize<Dictionary<string, string>>(obj.data.ToString());
                        if (!data.ContainsKey("username") || data["username"].Length < 1)
                        {
                            obj.client.Close();
                        }
                        else
                        {
                            obj.username.Append(Convert.ToString(((System.Net.IPEndPoint)obj.client.Client.RemoteEndPoint).Address));
                            Send("{\"status\": \"authorized\"}", obj);
                        }

                        obj.data.Clear();
                        obj.handle.Set();
                    }
                }
                catch (Exception ex)
                {
                    obj.data.Clear();
                    Log(ErrorMsg(ex.Message));
                    obj.handle.Set();
                }
            }
            else
            {
                obj.client.Close();
                obj.handle.Set();
            }
        }

        private void Connection(MyClient obj)
        {
                string name_connect = Convert.ToString(((System.Net.IPEndPoint)obj.client.Client.RemoteEndPoint).Address).ToString();
                clients.TryAdd(obj.id, obj);
                AddToGrid(obj.id, name_connect);
                string msg = string.Format("{0} Клиент подключён" + "\n", name_connect);
                Log(SystemMsg(msg));
                Send(SystemMsg(msg), obj.id);
                while (obj.client.Connected)
                {
                    try
                    {
                        obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(Read), obj);
                        obj.handle.WaitOne();
                    }
                    catch (Exception ex)
                    {
                        Log(ErrorMsg(ex.Message));
                    }
                }
                obj.client.Close();
                clients.TryRemove(obj.id, out MyClient tmp);
                RemoveFromGrid(tmp.id);
                msg = string.Format("{0} Клиент отключён" + "\n", name_connect);
                Log(SystemMsg(msg));
                Send(msg, tmp.id);
            
        }

        private void Listener(IPAddress ip, int port)
        {
            TcpListener listener = null;
            try
            {
                listener = new TcpListener(ip, port);
                listener.Start();
                Active(true);
                while (active)
                {
                    if (listener.Pending())
                    {
                        try
                        {
                            MyClient obj = new MyClient();
                            obj.id = id;
                            obj.username = new StringBuilder();
                            obj.client = listener.AcceptTcpClient();
                            obj.stream = obj.client.GetStream();
                            obj.buffer = new byte[obj.client.ReceiveBufferSize];
                            obj.data = new StringBuilder();
                            obj.handle = new EventWaitHandle(false, EventResetMode.AutoReset);
                            Thread th = new Thread(() => Connection(obj))
                            {
                                IsBackground = true
                            };
                            th.Start();
                            id++;
                        }
                        catch (Exception ex)
                        {
                            Log(ErrorMsg(ex.Message));
                        }
                    }
                    else
                    {
                        Thread.Sleep(500);
                    }
                }
                Active(false);
            }
            catch (Exception ex)
            {
                Log(ErrorMsg(ex.Message));
            }
            finally
            {
                if (listener != null)
                {
                    listener.Server.Close();
                }
            }
        }

        private void StartButton_Click(object sender, EventArgs e)
        {

        }

        private void Write(IAsyncResult result)
        {
            MyClient obj = (MyClient)result.AsyncState;
            if (obj.client.Connected)
            {
                try
                {
                    obj.stream.EndWrite(result);
                }
                catch (Exception ex)
                {
                    Log(ErrorMsg(ex.Message));
                }
            }
        }

        private void BeginWrite(string msg, MyClient obj) // send the message to a specific client
        {
            byte[] buffer = Encoding.GetEncoding(1251).GetBytes(msg);
            if (obj.client.Connected)
            {
                try
                {
                   
                    obj.stream.BeginWrite(buffer, 0, buffer.Length, new AsyncCallback(Write), obj);
                }
                catch (Exception ex)
                {
                    Log(ErrorMsg(ex.Message));
                }
            }
        }

        private void BeginWrite(string msg, long id = -1) // send the message to everyone except the sender or set ID to -1 to send to everyone
        {
            byte[] buffer = Encoding.GetEncoding(1251).GetBytes(msg);
            foreach (KeyValuePair<long, MyClient> obj in clients)
            {
                if (id != obj.Value.id && obj.Value.client.Connected)
                {
                    try
                    {
                        
                        obj.Value.stream.BeginWrite(buffer, 0, buffer.Length, new AsyncCallback(Write), obj.Value);
                    }
                    catch (Exception ex)
                    {
                        Log(ErrorMsg(ex.Message));
                    }
                }
            }
        }

        private void Send(string msg, MyClient obj)
        {
            if (send == null || send.IsCompleted)
            {
                send = Task.Factory.StartNew(() => BeginWrite(msg, obj));
            }
            else
            {
                send.ContinueWith(antecendent => BeginWrite(msg, obj));
            }
        }

        private void Send(string msg, long id = -1)
        {
            if (send == null || send.IsCompleted)
            {
                send = Task.Factory.StartNew(() => BeginWrite(msg, id));
            }
            else
            {
                send.ContinueWith(antecendent => BeginWrite(msg, id));
            }
        }



        private void Disconnect(long id = -1) // disconnect everyone if ID is not supplied or is -1
        {
            if (disconnect == null || !disconnect.IsAlive)
            {
                disconnect = new Thread(() =>
                {
                    if (id >= 0)
                    {
                        clients.TryGetValue(id, out MyClient obj);
                        obj.client.Close();
                        RemoveFromGrid(obj.id);
                    }
                    else
                    {
                        foreach (KeyValuePair<long, MyClient> obj in clients)
                        {
                            obj.Value.client.Close();
                            RemoveFromGrid(obj.Value.id);
                        }
                    }
                })
                {
                    IsBackground = true
                };
                disconnect.Start();
                
            }
        }


        private void Server_FormClosing(object sender, FormClosingEventArgs e)
        {
            exit = true;
            active = false;
            Disconnect();
        }

        private void ClientsDataGridView_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == clientsDataGridView.Columns["dc"].Index)
            {
                long.TryParse(clientsDataGridView.Rows[e.RowIndex].Cells["identifier"].Value.ToString(), out long id);
                Disconnect(id);
            }
        }

        private void ClearButton_Click(object sender, EventArgs e)
        {
            Log();
        }

        private void DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (serialPort.IsOpen)
            {
                if (serialPort.BytesToRead > 0 && serialPort.BytesToRead > 15)
                {

                    Data = serialPort.ReadLine();
                    label1.Text = Data.ToString();
                    SendTextBox_TEST();

                }
            }
        }

        private void Button1_Click(object sender, EventArgs e)
        {
            comboBoxCom.Items.Clear();
            string[] str = SerialPort.GetPortNames();
            foreach (string s in str)
            {
                comboBoxCom.Items.Add(s);
                comboBoxCom.SelectedIndex = 0;
            }
        }

        private void Start_Server()
        {
            if (active)
            {
                active = false;
            }
            else if (listener == null || !listener.IsAlive)
            {
                string address = IP_Adr;
                string number = portTextBox.Text.Trim();

                bool error = false;
                IPAddress ip = null;
                if (address.Length < 1)
                {
                    error = true;
                    Log(SystemMsg("Требуется адрес"));
                    
                }
                else
                {
                    try
                    {
                        ip = Dns.Resolve(address).AddressList[0];
                    }
                    catch
                    {
                        error = true;
                        Log(SystemMsg("Адрес не действителен"));
                        
                    }
                }
                int port = 0;
                if (number.Length < 1)
                {
                    error = true;
                    Log(SystemMsg("Требуется номер порта"));
                    
                }
                else if (!int.TryParse(number, out port))
                {
                    error = true;
                    Log(SystemMsg("Номер порта недопустим"));
                    
                }
                else if (port < 0 || port > 65535)
                {
                    error = true;
                    Log(SystemMsg("Номер порта находится вне зоны действия сети"));
                    
                }
                if (!error)
                {
                    listener = new Thread(() => Listener(ip, port))
                    {
                        IsBackground = true
                    };
                    listener.Start();
                    label_status_server.Text = "Сервер запущен. IP:" + address + " Port:" + number;

                    Label_Logo_Connect_lb.ForeColor = System.Drawing.Color.DarkGreen;
                    Label_Logo_Connect_lb.Text = "Server Available";
                }
            }
        }

        private void buttonOpenCloseCom_Click(object sender, EventArgs e)
        {
            if (!serialPort.IsOpen)// Если последовательный порт выключен
            {
                try
                {
                    if (comboBoxCom.SelectedIndex == -1)
                    {

                    }

                    string strSerialName = comboBoxCom.SelectedItem.ToString();
                    string strBaudRate = comboBoxBaudRate.SelectedItem.ToString();
                    string strDataBit = comboBoxDataBit.SelectedItem.ToString();
                    string strCheckBit = comboBoxCheckBit.SelectedItem.ToString();
                    string strStopBit = comboBoxStopBit.SelectedItem.ToString();

                    Int32 iBaudRate = Convert.ToInt32(strBaudRate);
                    Int32 iDataBit = Convert.ToInt32(strDataBit);

                    serialPort.PortName = strSerialName;// Имя порта
                    serialPort.BaudRate = iBaudRate;// Скорость передачи данных
                    serialPort.DataBits = iDataBit;// Биты данных



                    switch (strStopBit)            // Стоп-биты
                    {
                        case "1":
                            serialPort.StopBits = StopBits.One;
                            break;
                        case "1.5":
                            serialPort.StopBits = StopBits.OnePointFive;
                            break;
                        case "2":
                            serialPort.StopBits = StopBits.Two;
                            break;
                        default:


                            break;
                    }
                    switch (strCheckBit)           // Чётность
                    {
                        case "None":
                            serialPort.Parity = Parity.None;
                            break;
                        case "Odd":
                            serialPort.Parity = Parity.Odd;
                            break;
                        case "Even":
                            serialPort.Parity = Parity.Even;
                            break;
                        default:


                            break;
                    }


                    // Открываем последовательный порт
                    serialPort.Open();

                    // После открытия последовательного порта настройки больше не будут работать
                    comboBoxCom.Enabled = false;
                    comboBoxBaudRate.Enabled = false;
                    comboBoxDataBit.Enabled = false;
                    comboBoxCheckBit.Enabled = false;
                    comboBoxStopBit.Enabled = false;
                    button1.Enabled = false;

                    Start_Server();
                    
                }
                catch (System.Exception ex)
                {



                    Label_Logo_Connect_lb.ForeColor = System.Drawing.Color.Red;

                    return;
                }
            }
            else if(serialPort.IsOpen)
            {

                Thread.Sleep(50);

                serialPort.DiscardInBuffer(); //очистить буффер 
                Thread.Sleep(150);
                serialPort.Close();
                comboBoxCom.Enabled = true;
                comboBoxBaudRate.Enabled = true;
                comboBoxDataBit.Enabled = true;
                comboBoxCheckBit.Enabled = true;
                comboBoxStopBit.Enabled = true;
                button1.Enabled = true;
                label_status_server.Text = "";


                Label_Logo_Connect_lb.ForeColor = System.Drawing.Color.Red;




            }


        }

        private void SendTextBox_TEST()
        {

                    string msg = Data + "\n";
                    sendTextBox.Clear();
                    //Log(string.Format("{0} (Вы): {1}", usernameTextBox.Text.Trim(), msg));
                    Send(string.Format(msg));
                
            
        }

        private void button_send_Click(object sender, EventArgs e)
        {
            if(sendTextBox.TextLength != 0)
            {
                string msg = sendTextBox.Text;
                sendTextBox.Clear();
                Log(string.Format("(Вы):" + msg));
                Send(string.Format(msg));
            }

        }


        private void disconnectButton_Click(object sender, EventArgs e)
        {
            Disconnect();
        }
    }
}
