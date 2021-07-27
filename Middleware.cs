using JamaaTech.Smpp.Net.Client;
using JamaaTech.Smpp.Net.Lib;
using JamaaTech.Smpp.Net.Lib.Protocol;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SMPPMiddleware
{
    class Middleware
    {
        public static DateTime submittime { get; set; }
        public static DateTime donetime { get; set; }
        public static string msgid { get; set; }
        public static string status { get; set; }
        public static string statusCode { get; set; }
        public static string errorCode { get; set; }
        CultureInfo provider = CultureInfo.InvariantCulture;
        public static string response { get; set; }
        public static string alertid { get; set; }

        public static TcpListener server = null;
        public static SmppClient smppclient = null;
       
        public static void StartService()
        {
            tgatedbContext mc = new tgatedbContext();
            try
            {
                smppclient = new JamaaTech.Smpp.Net.Client.SmppClient();
                smppclient.Properties.SystemID = ConfigurationManager.AppSettings["SystemID"];
                smppclient.Properties.Password = ConfigurationManager.AppSettings["SystemPwd"];
                smppclient.Properties.Port = Convert.ToInt32(ConfigurationManager.AppSettings["SMPPPort"]);
                smppclient.Properties.Host = ConfigurationManager.AppSettings["SMPPServerIP"];
                smppclient.Properties.DefaultEncoding = DataCoding.SMSCDefault;
                smppclient.Properties.AddressNpi = NumberingPlanIndicator.Unknown;
                smppclient.Properties.AddressTon = TypeOfNumber.Unknown;
                smppclient.Properties.SystemType = "transceiver";
                smppclient.Properties.DefaultServiceType = ServiceType.DEFAULT;// "transceiver";    
                smppclient.KeepAliveInterval = 30000;
                smppclient.AutoReconnectDelay = 1000;

                smppclient.MessageSent += delegate (object sender, MessageEventArgs e) { Client_MessageSent(sender, e, alertid); };
                smppclient.MessageReceived += Client_MessageReceived;
                smppclient.MessageDelivered += delegate (object sender, MessageEventArgs e) { Client_MessageDelivered(sender,e,alertid); };

                smppclient.Start();
                if (smppclient.ConnectionState != SmppConnectionState.Connected)
                    smppclient.ForceConnect();

                //Console.WriteLine("SMPP Client now connected");
                //Logging("SMPP Client now connected");
                EventLog eve = new EventLog();
                eve.Source = "SMPP Middleware - StartService";
                eve.WriteEntry("SMPP Client now connected", EventLogEntryType.Information);
                server = new TcpListener(IPAddress.Parse(ConfigurationManager.AppSettings["ServiceIP"]), Convert.ToInt32(ConfigurationManager.AppSettings["ServicePort"]));
                server.Start();
                //Console.WriteLine("TCP Listener Started.");
                //Logging("TCP Listener Started.");
                EventLog eventLog = new EventLog();
                eventLog.Source = "SMPP Middleware - StartService";
                eventLog.WriteEntry("TCP Listener Started.", EventLogEntryType.Information);
                var whileThread = new Thread(() =>
                {
                    while (true)
                    {
                        try
                        {
                            Socket client = server.AcceptSocket();
                            var childSocketThread = new Thread(() =>
                            {
                                submittime = DateTime.Now;
                                byte[] data = new byte[2048];
                                int size = client.Receive(data);
                                var str = System.Text.Encoding.Default.GetString(data);
                                string[] elements = str.ToString().Split('|');
                                alertid = elements[3];
                                try
                                {
                                    var textMessage = new TextMessage() { DestinationAddress = elements[0], SourceAddress = elements[2], Text = elements[1] };
                                    textMessage.RegisterDeliveryNotification = true;
                                    textMessage.UserMessageReference = alertid;
                                    textMessage.SubmitUserMessageReference = false;
                                    smppclient.SendMessage(textMessage);

                                    
                                    string full =textMessage.ReceiptedMessageId.ToString() +"|"+ textMessage.UserMessageReference.ToString();
                                byte[] q = Encoding.ASCII.GetBytes(full);
                                    client.Send(q);
                                }
                                catch (Exception e)
                                {
                                    //Logging(ex.ToString());
                                    EventLog ex = new EventLog();
                                    eve.Source = "SMPP Middleware - StartService";
                                    eve.WriteEntry(e.ToString(), EventLogEntryType.Error);
                                  
                                }
                           
                            client.Close();
                            });
                            childSocketThread.Start();
                        }catch(Exception ex)
                        {
                            EventLog el = new EventLog();
                            el.Source = "SMPP Middleware - StartService";
                            el.WriteEntry(ex.ToString(), EventLogEntryType.Error);
                        }
                    }
                });
                whileThread.Start();
            }
            catch (SmppBindException e)
            {
                try
                {
                    EventLog eve = new EventLog();
                    eve.Source = "SMPP Middleware - SMPP Bind Exception";
                    eve.WriteEntry(e.ToString(), EventLogEntryType.Error);
                }
               
                catch (Exception ev)
                {
                    EventLog eve = new EventLog();
                    eve.Source = "SMPP Middleware - StartService";
                    eve.WriteEntry(ev.ToString(), EventLogEntryType.Error);
                }
            }
            catch (SocketException ex)
            {
                try
                {
                    EventLog eve = new EventLog();
                    eve.Source = "SMPP Middleware - Socket Exception";
                    eve.WriteEntry(ex.ToString(), EventLogEntryType.Error);
                }
                catch (Exception e)
                {
                    EventLog eve = new EventLog();
                    eve.Source = "SMPP Middleware - General Exception";
                    eve.WriteEntry(e.ToString(), EventLogEntryType.Error);
                }
                EventLog eventLog = new EventLog();
                eventLog.Source = "SMPP Middleware - Service Error";
                eventLog.WriteEntry(ex.ToString(), EventLogEntryType.Error);
            }
            catch (Exception ex)
            {
                //Logging(ex.ToString());
                EventLog eventLog = new EventLog();
                eventLog.Source = "SMPP Middleware - General Exception";
                eventLog.WriteEntry(ex.ToString(), EventLogEntryType.Error);
            }
        }

        public static void StopService()
        {
            try
            {
                smppclient.Shutdown();
                server.Stop();
            }catch(Exception ex)
            {
              //  Logging(ex.ToString());
                EventLog eventLog = new EventLog();
                eventLog.Source = "SMPP Middleware - StopService";
                eventLog.WriteEntry(ex.ToString(), EventLogEntryType.Error);
            }
        }
        public static void SendMessageCompleteCallback(IAsyncResult result)
        {
            Console.WriteLine("Send msg completed");
            SmppClient client = (SmppClient)result.AsyncState;
            client.EndSendMessage(result);
        }
        public static void Client_MessageDelivered(object sender, MessageEventArgs e, string senderid)
        {
            try
            {
                TextMessage textMsg = e.ShortMessage as JamaaTech.Smpp.Net.Client.TextMessage;
               
                Middleware.response = textMsg.ToString() + "|"+senderid;
                string[] dr = response.Split(' ');
                string[] recid = dr[0].Split(':');
                string[] submitt = dr[4].Split(':');
                string[] donet = dr[6].Split(':');
                string[] status1 = dr[7].Split(':');
                string[] scode = dr[2].Split(':');
                string[] err = dr[8].Split(':');
               
                status = status1[1];
                statusCode = scode[1];
                errorCode = err[1];
                
                try
                {
                    //Store Delivery response in Database
                    using (dbContext mc = new dbContext())
                    {
                        delivery_report delivery = new delivery_report();
                        delivery.msgid = recid[1];
                        delivery.senderid = senderid;
                        delivery.delivery_status = status;
                        delivery.status_code = statusCode;
                        delivery.error_code = errorCode;
                        delivery.submittime = DateTime.Now;
                        delivery.donetime = DateTime.Now;
                        mc.delivery_report.Add(delivery);
                        mc.SaveChanges();
                    }
                    EventLog eventLog = new EventLog();
                    eventLog.Source = "SMPPMiddleware - MessageDelivered Event";
                    eventLog.WriteEntry(Middleware.response, EventLogEntryType.Information);
                }catch(Exception ex)
                {
                    string path = ConfigurationManager.AppSettings["deliveryReport"] + recid[1] + ".txt";
                    string str = recid[1] + "|" + submittime.ToString("yyyyMMddHHmmssfff") + "|" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + "|" + status + "|" + statusCode + "|" + errorCode;
                    File.WriteAllText(path, str);
                    EventLog eventLog = new EventLog();
                    eventLog.Source = "SMPP Middleware - MessageDelivered Event";
                    eventLog.WriteEntry(ex.ToString(), EventLogEntryType.Error);
                }
                
            }
            catch(Exception ex)
            {
              //  Logging(ex.ToString());
                EventLog eventLog = new EventLog();
                eventLog.Source = "SMPP Middleware - MessageDelivered Event";
                eventLog.WriteEntry(ex.ToString(), EventLogEntryType.Error);
            }
        }

        private static void Client_MessageReceived(object sender, MessageEventArgs e)
        {
            try
            {
                //The event argument e contains more information about the received message       
                TextMessage textMsg = e.ShortMessage as JamaaTech.Smpp.Net.Client.TextMessage; //This is the received text message                  

                //richTextBox1.Text = richTextBox1.Text + textMsg + " Step 1";
                string msgInfo = string.Format("Sender: {0}, Receiver: {1}, Message content: {2}", textMsg.SourceAddress, textMsg.DestinationAddress, textMsg.Text);


            }catch(Exception ex)
            {
                EventLog eventLog = new EventLog();
                eventLog.Source = "SMPPMiddleware - MessageReceived Event";
                eventLog.WriteEntry(ex.ToString(), EventLogEntryType.Error);
            }
        }

        private static void Client_MessageSent(object sender1, MessageEventArgs e, string senderid)
        {
            try
            {
                TextMessage textMsg = e.ShortMessage as JamaaTech.Smpp.Net.Client.TextMessage;
                TextMessage msg = e.ShortMessage as TextMessage;

                string SegID = msg.SegmentID.ToString(); //Gives the message ID from the SMPP on Send 

                string Seg = msg.SequenceNumber.ToString(); //Give the status of the message - Enroute
                msgid = msg.ReceiptedMessageId;
                sender1 = msg.ReceiptedMessageId;
                string mstate = msg.MessageState.ToString();
                string msgref = msg.UserMessageReference.ToString();
                
                string now = DateTime.Now.ToString("yyyyMMddHHmmssfff");
                string str = msgid + "|" + senderid + "|" + msgref;
                EventLog eventLog = new EventLog();
                eventLog.Source = "SMPP Middleware - MessageSent Event";
                eventLog.WriteEntry(str, EventLogEntryType.Information);
                //File.WriteAllText(path, str);
            }catch(Exception ex)
            {
                EventLog eventLog = new EventLog();
                eventLog.Source = "SMPP Middleware - MessageSent Event";
                eventLog.WriteEntry(ex.ToString(), EventLogEntryType.Error);
            }
        }

       

    }
}
