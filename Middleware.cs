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
//using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace tglSMPPMiddleware
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
                eve.Source = "tglSMPPMiddleware - StartService";
                eve.WriteEntry("SMPP Client now connected", EventLogEntryType.Information);
                server = new TcpListener(IPAddress.Parse(ConfigurationManager.AppSettings["ServiceIP"]), Convert.ToInt32(ConfigurationManager.AppSettings["ServicePort"]));
                server.Start();
                //Console.WriteLine("TCP Listener Started.");
                //Logging("TCP Listener Started.");
                EventLog eventLog = new EventLog();
                eventLog.Source = "tglSMPPMiddleware - StartService";
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
                                //Logging(str);
                                string[] elements = str.ToString().Split('|');
                                alertid = elements[3];
                            //Console.WriteLine("Receved data:{0} ", str.ToString());
                            //Logging(string.Format("Received data:{0} ", str.ToString()));
                                try
                                {
                                    var textMessage = new TextMessage() { DestinationAddress = elements[0], SourceAddress = elements[2], Text = elements[1] };
                                    textMessage.RegisterDeliveryNotification = true;
                                    textMessage.UserMessageReference = alertid;
                                    textMessage.SubmitUserMessageReference = false;
                                    smppclient.SendMessage(textMessage);

                                    //using (tgatedbContext db = new tgatedbContext())
                                    //{
                                    //    message_mapping mm = new message_mapping();
                                    //    mm.msgid = textMessage.ReceiptedMessageId;
                                    //    mm.guid = textMessage.UserMessageReference;
                                    //    mm.inserttime = DateTime.Now;
                                    //    mc.message_mapping.Add(mm);
                                    //    db.SaveChanges();
                                    //}
                                    string full =textMessage.ReceiptedMessageId.ToString() +"|"+ textMessage.UserMessageReference.ToString();
                                byte[] q = Encoding.ASCII.GetBytes(full);
                                    client.Send(q);
                                }
                                catch (Exception e)
                                {
                                    //Logging(ex.ToString());
                                    EventLog ex = new EventLog();
                                    eve.Source = "tglSMPPMiddleware - StartService";
                                    eve.WriteEntry(e.ToString(), EventLogEntryType.Error);
                                    //Console.WriteLine(ex.ToString());
                                }
                            //while (string.IsNullOrEmpty(msgid)) { Thread.Sleep(1); }
                            //Console.WriteLine("Waiting for incoming data...");
                            client.Close();
                            });
                            childSocketThread.Start();
                        }catch(Exception ex)
                        {
                            EventLog el = new EventLog();
                            el.Source = "tglSMPPMiddleware - StartService";
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
                    service_activity_log sal = new service_activity_log();
                    sal.activity_time = DateTime.Now;
                    sal.alertid = "tgl SMPP Middleware - SMPP Connection Error";
                    sal.activity_log = "Error while Binding to SMPP Server: " + e.ToString();
                    mc.service_activity_log.Add(sal);
                    mc.SaveChanges();
                }
                catch (SqlException ev)
                {
                    // Logging(ev.ToString());
                    EventLog eve = new EventLog();
                    eve.Source = "tglSMPPMiddleware - StartService";
                    eve.WriteEntry(ev.ToString(), EventLogEntryType.Error);
                }
                catch (Exception ev)
                {
                    EventLog eve = new EventLog();
                    eve.Source = "tglSMPPMiddleware - StartService";
                    eve.WriteEntry(ev.ToString(), EventLogEntryType.Error);
                    // Logging(ev.ToString());
                    // Logging(e.ToString());
                }
            }
            catch (SocketException ex)
            {
                try
                {
                    service_activity_log sal = new service_activity_log();
                    sal.activity_time = DateTime.Now;
                    sal.alertid = "tgl SMPP Middleware - Connection Error";
                    sal.activity_log = "Error while seting up connection on SMPP Middleware: " + ex.ToString();
                    mc.service_activity_log.Add(sal);
                    mc.SaveChanges();
                }
                catch (SqlException e)
                {
                    //Logging(e.ToString());
                    EventLog eve = new EventLog();
                    eve.Source = "tglSMPPMiddleware - StartService";
                    eve.WriteEntry(e.ToString(), EventLogEntryType.Error);
                }
                catch (Exception e)
                {
                    EventLog eve = new EventLog();
                    eve.Source = "tglSMPPMiddleware - StartService";
                    eve.WriteEntry(e.ToString(), EventLogEntryType.Error);
                }
                //Logging(ex.InnerException.ToString());
                EventLog eventLog = new EventLog();
                eventLog.Source = "tglSMPPMiddleware - StartService";
                eventLog.WriteEntry(ex.ToString(), EventLogEntryType.Error);
            }
            catch (Exception ex)
            {
                //Logging(ex.ToString());
                EventLog eventLog = new EventLog();
                eventLog.Source = "tglSMPPMiddleware - StartService";
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
                eventLog.Source = "tglSMPPMiddleware - StopService";
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
                //Console.WriteLine("Message State Event: " + textMsg.MessageState.ToString());
                //Console.WriteLine("Message Delivered Event: " + textMsg.Text);
                Middleware.response = textMsg.ToString() + "|"+senderid;

                string[] dr = response.Split(' ');
                string[] recid = dr[0].Split(':');
                string[] submitt = dr[4].Split(':');
                string[] donet = dr[6].Split(':');
                string[] status1 = dr[7].Split(':');
                string[] scode = dr[2].Split(':');
                string[] err = dr[8].Split(':');
                //Console.WriteLine("Delivery Response Received: " + response);
                //submittime = DateTime.ParseExact(submitt[1], "yyMMddHHmm", CultureInfo.InvariantCulture);
                //donetime = DateTime.ParseExact(donet[1], "yyMMddHHmm", CultureInfo.InvariantCulture);
                status = status1[1];
                statusCode = scode[1];
                errorCode = err[1];
                //Console.WriteLine("Submit date:{0},  Done Date: {1}, Delivery State:{2}, Status Code:{3}, Error Code:{4}", submittime, donetime, status, statusCode, errorCode);
                try
                {
                    using (tgatedbContext mc = new tgatedbContext())
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
                    eventLog.Source = "tglSMPPMiddleware - MessageDelivered Event";
                    eventLog.WriteEntry(Middleware.response, EventLogEntryType.Information);
                }catch(Exception ex)
                {
                    string path = ConfigurationManager.AppSettings["deliveryReport"] + recid[1] + ".txt";
                    string str = recid[1] + "|" + submittime.ToString("yyyyMMddHHmmssfff") + "|" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + "|" + status + "|" + statusCode + "|" + errorCode;
                    File.WriteAllText(path, str);
                    EventLog eventLog = new EventLog();
                    eventLog.Source = "tglSMPPMiddleware - MessageDelivered Event";
                    eventLog.WriteEntry(ex.ToString(), EventLogEntryType.Error);
                }
                //Logging(string.Format("Submit date:{0},  Done Date: {1}, Delivery State:{2}, Status Code:{3}, Error Code:{4}", submittime, donetime, status, statusCode, errorCode));
                //string path = ConfigurationManager.AppSettings["deliveryReport"] + recid[1] + ".txt";
                //string str = recid[1] + "|" + submittime.ToString("yyyyMMddHHmmssfff") + "|" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + "|" + status + "|" + statusCode + "|" + errorCode;
                //File.WriteAllText(path, str);
            }
            catch(Exception ex)
            {
              //  Logging(ex.ToString());
                EventLog eventLog = new EventLog();
                eventLog.Source = "tglSMPPMiddleware - MessageDelivered Event";
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

                //Display message       
                //Logging("Message Received Event: " + msgInfo);
                //Console.WriteLine("Message Received Event: " + msgInfo);
            }catch(Exception ex)
            {
                EventLog eventLog = new EventLog();
                eventLog.Source = "tglSMPPMiddleware - MessageReceived Event";
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
                //Console.WriteLine("Message Sent Event: " + msg.Text + " Segment ID: " + SegID + " Sequence Number: " + Seg + " Message ID: " + msgid + " Message State: " + mstate); //Display message
                
                //using(tgatedbContext mc = new tgatedbContext())
                //{
                //    message_mapping mm = new message_mapping();
                //    mm.msgid = msgid;
                //    mm.guid = msgref;
                //    mm.inserttime = DateTime.Now;
                //    mc.message_mapping.Add(mm);
                //    mc.SaveChanges();
                //}
                //string path = ConfigurationManager.AppSettings["receiveConfirmation"] + msgid + ".txt";
                string now = DateTime.Now.ToString("yyyyMMddHHmmssfff");
                string str = msgid + "|" + senderid + "|" + msgref;
                EventLog eventLog = new EventLog();
                eventLog.Source = "tglSMPPMiddleware - MessageSent Event";
                eventLog.WriteEntry(str, EventLogEntryType.Information);
                //File.WriteAllText(path, str);
            }catch(Exception ex)
            {
                EventLog eventLog = new EventLog();
                eventLog.Source = "tglSMPPMiddleware - MessageSent Event";
                eventLog.WriteEntry(ex.ToString(), EventLogEntryType.Error);
            }
        }

        //public static void Logging(string str)
        //{
        //    try
        //    {
        //        string path = ConfigurationManager.AppSettings["logFilePath"] + DateTime.Now.ToString("yyyyMMdd") + ".log";
        //        File.WriteAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +": "+ str + "\n");
        //    }catch(Exception ex)
        //    {
        //        EventLog eventLog = new EventLog();
        //        eventLog.Source = "tglSMPPMiddleware - Logging";
        //        eventLog.WriteEntry(ex.ToString(), EventLogEntryType.Error);
        //    }
        //}

    }
}
