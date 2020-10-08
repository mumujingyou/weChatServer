using LitJson;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Xml;

namespace WeChatPayServer
{
    /// <summary>
    /// 负责微信支付相关的接口
    /// </summary>
   public class WechatPay
    {
        static WechatPay instance;
        public static WechatPay Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new WechatPay();
                }
                return instance;
            }
        }

        //密钥 在商户后台自己配置的
        static string miyao = "jflkdwoakdlxvnawrfgwt11262357895";

        string appId = "wxc0d38c38f13506d4";
        string merchantId = "1495064892";

        //接收充值结果的URL
        string notify_url = @"http://193.112.44.199:7983";
        //请求支付参数的URL 官方提供 固定的URL
        string getWeChatPayParameterURL = @"https://api.mch.weixin.qq.com/pay/unifiedorder";

        /// <summary> 获取时间戳 </summary>
        public int ConvertDateTimeInt(System.DateTime time)
        {
            System.DateTime startTime =
                TimeZone.CurrentTimeZone.ToLocalTime(new System.DateTime(1970, 1, 1));
            Console.WriteLine("计算后的时间戳是：" + (int)(time - startTime).TotalSeconds);
            return (int)(time - startTime).TotalSeconds;
        }

        internal void Pay(Agent agent)
        {
            OrderInfo orderInfo = new OrderInfo();
            orderInfo.appid = appId;
            orderInfo.mch_id = merchantId;
            orderInfo.body = "秦始皇的时装(活动限版)";//商品描述
            orderInfo.total_fee =100; //支付金额 单位:分 100=10mao=1yuan
            orderInfo.spbill_create_ip ="127.0.0.1";//用户终端实际IP
            orderInfo.notify_url = notify_url;//支付结果通知地址
            orderInfo.trade_type = "APP";//交易类型

            //随机字符串 不长于32位 不能使用'.'符号拼接,如IP:127.0.0.1
            orderInfo.nonce_str = "nonceStr" + ConvertDateTimeInt(DateTime.Now);

            //商户订单号 要求唯一性 
            orderInfo.out_trade_no = "wxpay" + ConvertDateTimeInt(DateTime.Now);

            orderInfo.clientAgent = agent;

            //第一步:签名计算=>获取向微信服务器请求的参数
            Dictionary<string, string> dics = new Dictionary<string, string>();
            dics.Add("appid", orderInfo.appid);
            dics.Add("mch_id", orderInfo.mch_id);
            dics.Add("nonce_str", orderInfo.nonce_str);
            dics.Add("body", orderInfo.body);
            dics.Add("out_trade_no", orderInfo.out_trade_no);
            dics.Add("total_fee", orderInfo.total_fee.ToString());
            dics.Add("spbill_create_ip", orderInfo.spbill_create_ip);
            dics.Add("notify_url", orderInfo.notify_url);
            dics.Add("trade_type", orderInfo.trade_type);

            string tempXML = GetParamSrc(dics);

            //第二步:下单请求-获取响应的参数
            string result = GetWeChatPayParameter(tempXML);

            //第三步:将返回的参数再进行签名 并且按照我们跟客户端协定好的格式拼接
            string payList = PayOrder(result, orderInfo);

            if (payList!="error")
            {
                Console.WriteLine("---------------------");
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine("将参数传递给客户端：" + payList);
                Console.WriteLine("---------------------");
                agent.Send(payList);
            }
            dics.Clear();
        }

        Dictionary<string, string> dic = new Dictionary<string, string>();
        //out_trade_no内部订单号  ->  订单信息
        Dictionary<string, OrderInfo> orderDic = new Dictionary<string, OrderInfo>();
        /// <summary>
        /// 计算客户端调起微信支付所需要的参数
        /// </summary>
        /// <param name="str">微信服务器返回的数据</param>
        /// <returns>由参数加逗号拼接的字符串</returns>
        public string PayOrder(string str, OrderInfo orderinfo)
        {
            //微信支付返回的是XML 需要进行解析
            XmlDocument doc = new XmlDocument();
            //防止xml被外部注入修改
            doc.XmlResolver = null;
            doc.LoadXml(str);
            XmlNode xml = doc.DocumentElement;
            //状态码:SUCCESS 成功   FAIL 失败
            if (xml["return_code"].InnerText == "FAIL")//获取预支付单号失败
            {
                Console.WriteLine("请求预支付单号异常:" + xml["return_msg"].InnerText);
                //实际上这里对错误 不应该直接处理 而是需要根据错误原因做相应的逻辑
                //如充值单号如果重复了 随机字符串如果重复了 就重新生成
                //但是 像这类异常 应该是在请求之前 就有相应的策略 而不应该等到这里来响应处理
                //错误码:https://pay.weixin.qq.com/wiki/doc/api/app/app.php?chapter=9_1
                return "error";
            }

            //请求参数:https://pay.weixin.qq.com/wiki/doc/api/app/app.php?chapter=9_12&index=2
            //解析得到以下的这些参数 再进行二次签名
            dic.Add("appid", xml["appid"].InnerText);
            dic.Add("partnerid", xml["mch_id"].InnerText);
            dic.Add("prepayid", xml["prepay_id"].InnerText);
            dic.Add("noncestr", xml["nonce_str"].InnerText);
            dic.Add("package", "Sign=WXPay");

            string timeStamp = ConvertDateTimeInt(DateTime.Now).ToString();
            dic.Add("timestamp", timeStamp);
            string sign = GetParamSrc(dic);

            //缓存订单信息  以便于在微信结果出来后 进行对比校验
            orderinfo.prepay_id = xml["prepay_id"].InnerText;
            orderinfo.sign = sign;
            orderDic.Add(orderinfo.out_trade_no, orderinfo);

            //将客户端所需要的参数进行返回
            //string msg = xml["appid"].InnerText + "," + xml["mch_id"].InnerText + "," + xml["prepay_id"].InnerText + ","
            //    + xml["nonce_str"].InnerText + "," + timeStamp + "," + "Sign=WXPay" + "," + sign;
            WeChatPayModel model = new WeChatPayModel();
            model.appid = xml["appid"].InnerText;
            model.mch_id = xml["mch_id"].InnerText;
            model.prepayid = xml["prepay_id"].InnerText;
            model.noncestr = xml["nonce_str"].InnerText;
            model.timestamp = timeStamp;
            model.packageValue = "Sign=WXPay";//扩展参数 暂时没什么用
            model.sign = sign;

            //序列化成json字符串 返回给客户端 客户端再进行反序列化 客户端传递给sdk的支付接口
           string msg = JsonMapper.ToJson(model);

            dic.Clear();//清空本次的数据
            return msg;
        }

        /// <summary> 请求客户端在微信支付时候所需的参数 </summary>
        private string GetWeChatPayParameter(string postData)
        {
            //-----------------------------------第一步:创建Htt请求----------------------//
            //向微信发起支付参数的请求
            HttpWebRequest request = null;
            if (getWeChatPayParameterURL.StartsWith("https", StringComparison.OrdinalIgnoreCase))
            {
                //创建WebRequest请求
                request = WebRequest.Create(getWeChatPayParameterURL) as HttpWebRequest;
                //设置用于验证服务器证书的回调
                ServicePointManager.ServerCertificateValidationCallback = new RemoteCertificateValidationCallback(CheckValidationResult);
                //设置HTTP版本
                request.ProtocolVersion = HttpVersion.Version11;
                // 这里设置了安全协议类型。
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;// SecurityProtocolType.Tls1.2; 
                                                                                  //false表示不建立持续性连接
                request.KeepAlive = false;
                //检查已吊销的证书
                ServicePointManager.CheckCertificateRevocationList = true;
                //URP最大的并发连接数量
                ServicePointManager.DefaultConnectionLimit = 100;
                //参考: https://msdn.microsoft.com/zh-cn/library/system.net.servicepoint.expect100continue
                ServicePointManager.Expect100Continue = false;
            }
            //如果开头不是https 直接创建web请求
            else
            {
                request = (HttpWebRequest)WebRequest.Create(getWeChatPayParameterURL);
            }
            //web请求的一些属性设置
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            request.Referer = null;
            request.AllowAutoRedirect = true;
            request.UserAgent = "Mozilla/4.0 (compatible; MSIE 7.0; Windows NT 5.2; .NET CLR 1.1.4322; .NET CLR 2.0.50727)";
            request.Accept = "*/*";

            //通过流的形式提交网络数据的请求 简单说就是往URL里提交数据
            byte[] data = Encoding.UTF8.GetBytes(postData);
            Stream newStream = request.GetRequestStream();
            //流要写入的数据和长度
            newStream.Write(data, 0, data.Length);
            newStream.Close();

            //-------------------------------第二步:获取请求响应的结果-----------------//
            //获取网页响应结果
            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            Stream stream = response.GetResponseStream();
            string result = string.Empty;
            //接收到网络消息 就进一步加工处理
            using (StreamReader sr = new StreamReader(stream))
            {
                //从读取流中取出微信服务器返回的数据
                result = sr.ReadToEnd();
            }

            //-------------------------------第三步:根据返回的结果,计算客户端最终需要的参数-----------------//
            //将返回的参数进一步计算出客户端本次支付需要的实际参数
            //并且根据协议格式 拼接客户端可以识别的网络消息
            return result;//读取微信返回的数据
        }

        /// <summary> 设置用于验证服务器证书的回调 </summary>
        private bool CheckValidationResult(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

        /// <summary> 签名算法 </summary>
        public string GetParamSrc(Dictionary<string, string> dic)
        {
            //-----------第一步:对参数按照key=value的格式，并按照参数名ASCII字典序排序-----//

            //格式:键=值&键=值... 意义:获取sign参数
            StringBuilder str = new StringBuilder();

            //排序:升序
            var param1 = dic.OrderBy(x => x.Key).ToDictionary(x => x.Key, y => y.Value);

            //再从字典中 获取各个元素 拼接为XML的格式 键跟值之间 用"="连接起来
            foreach (string dic1 in param1.Keys)
            {
                str.Append(dic1 + "=" + dic[dic1] + "&");
            }

            //-----------------第二步:拼接商户密钥 获取签名sign-----------------------------//
            str.Append("key=" + miyao);
            //把空字符串给移除替换掉 得到获取sign的字符串
            string getSignStr = str.ToString().Replace(" ", "");
            Console.WriteLine("第一次准备获取签名的字符串{0}", getSignStr);

            //从这里开始 是对str字符串进行MD5加密
            MD5 md5 = new MD5CryptoServiceProvider();
            byte[] bytValue, bytHash;
            bytValue = Encoding.UTF8.GetBytes(getSignStr);
            bytHash = md5.ComputeHash(bytValue);
            md5.Clear();  //释放掉MD5对象
            string tempStr = "";
            //按16进制的格式 将字节数组转化为等效字符串
            for (int i = 0; i < bytHash.Length; i++)
            {
                tempStr += bytHash[i].ToString("X").PadLeft(2, '0');
            }
            //转化为大写 得到 sign签名参数
            string sign = tempStr.ToUpper();

            //------------------第三步 返回XML格式的字符串--------------------------//
            StringBuilder xmlStr = new StringBuilder();
            xmlStr.Append("<xml>");
            foreach (string dic1 in param1.Keys)
            {
                xmlStr.Append("<" + dic1 + ">" + dic[dic1] + "</" + dic1 + ">");
            }

            //追加到XML尾部
            xmlStr.Append("<sign>" + sign + "</sign></xml>");
            Console.WriteLine("预支付请求参数:" + xmlStr.ToString().Replace(" ", ""));
            return xmlStr.ToString().Replace(" ", "");
        }

        //-----------------------------以上是支付参数的请求--------------------//

        //-----------------------------以下是支付结果的监听--------------------//
        HttpListener httpListener; //http监听对象
        string Order_Url = @"http://172.16.0.5:7983/";//监听的url

        public WechatPay()
        {
            //HTP监听对象 监听微信给Order_Url地址发送的反馈
            if (httpListener == null)
            {
                httpListener = new HttpListener();
                httpListener.Prefixes.Add(Order_Url);
                httpListener.Start();
                httpListener.BeginGetContext(new AsyncCallback(GetContextCallback), null);
            }
        }

        private void GetContextCallback(IAsyncResult ar)
        {
            try
            {
                HttpListenerContext context = httpListener.EndGetContext(ar);
                HttpListenerRequest request = context.Request;
                if (context != null)
                {
                    StreamReader body = new StreamReader(request.InputStream, Encoding.UTF8);//读取流，用来获取微信请求的数据
                    string pay_notice = HttpUtility.UrlDecode(body.ReadToEnd(), Encoding.UTF8);//HttpUtility.UrlDecode：解码                                                  //打印看看支付宝给我们发了什么
                    Console.WriteLine("微信通知结果来了:" + pay_notice);

                    //响应微信的消息
                    HttpListenerResponse response = context.Response;

                    //微信支付返回的是XML 需要进行解析
                    XmlDocument doc = new XmlDocument();
                    //防止xml被外部注入修改
                    doc.XmlResolver = null;
                    doc.LoadXml(pay_notice);
                    XmlNode xml = doc.DocumentElement;
                    //状态码:SUCCESS 成功   FAIL 失败
                    if (xml["return_code"].InnerText == "SUCCESS")
                    {
                        OrderInfo checkData;
                        //检查是否存在订单号
                        if (orderDic.TryGetValue(xml["out_trade_no"].InnerText,out checkData))
                        {
                            //out_trade_no 商户订单号
                            Console.WriteLine("支付成功:" + xml["return_code"].InnerText);
                            if (xml["out_trade_no"].InnerText.Contains(checkData.out_trade_no))
                            {
                                //if (xml["sign"].InnerText.Contains(checkData.sign))
                                //{
                                //安全验证 单号对应的金额 
                                if (int.Parse(xml["total_fee"].InnerText) == checkData.total_fee)
                                {

                                    Console.WriteLine("微信支付结果验证,单号金额一致");
                                    //消息协议 SendProps,会话ID/订单号，道具ID,数量...(道具实体)
                                    //向客户端发送道具
                                    string out_trade_no = xml["out_trade_no"].InnerText;
                                    checkData.clientAgent.Send("支付成功");
                                }
                                else
                                {
                                    Console.WriteLine("微信支付结果验证,单号金额不一致");
                                }
                            }
                            else
                            {
                                Console.WriteLine("订单不匹配，商户订单号:{0},error:{1}", xml["out_trade_no"].InnerText, xml["err_code"].InnerText);
                            }
                        }
                        else
                        {
                            checkData = null;
                        }
                        //回应给微信服务器
                        string responseStr = @"<xml><return_code><![CDATA[SUCCESS]]></return_code><return_msg><![CDATA[OK]]></return_msg></xml>";
                        byte[] buffer = Encoding.UTF8.GetBytes(responseStr);
                        response.ContentLength64 = buffer.Length;
                        Stream output = response.OutputStream;
                        output.Write(buffer, 0, buffer.Length);
                        output.Close();
                        response.Close();
                    }
                    else
                    {
                        Console.WriteLine("支付失败:" + xml["return_msg"].InnerText);
                        //告诉客户端 支付失败以及具体的原因的
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                return;
            }

            if (httpListener.IsListening)
            {
                try
                {
                    httpListener.BeginGetContext(new AsyncCallback(GetContextCallback), null);
                }
                catch (Exception e)
                {

                    Console.WriteLine(e.ToString());
                }
            }
        }
    }

    /// <summary>
    /// 订单的数据模型: https://pay.weixin.qq.com/wiki/doc/api/app/app.php?chapter=9_1
    /// </summary>
    public class OrderInfo
    {
        public string appid = "";//创建的应用ID
        public string mch_id = "";//商户ID
        public string nonce_str = "";//随机字符串 不长于32位

        public string body = "";//商品描述
        public string out_trade_no;//订单号商户系统内部订单号，要求32个字符内，只能是数字、大小写字母_-|*@ ，且在同一个商户号下唯一。
        public int total_fee;//1000分=1块钱
        public string spbill_create_ip;//用户终端实际IP
        public string notify_url;//139.196.112.69
        public string trade_type = "APP";
        public string scene_info = "大餐厅腾讯";//这里可以填写实际场景信息


        public string prepay_id;//预支付单号
        public string sign;//最终给客户端的签名

        public Agent clientAgent;//本次发起支付的客户端

        //public string objID;//道具ID
        //public int objCount;//道具数量

        public string transaction_id;//结果通知时返回的微信支付订单号
    }


    public class WeChatPayModel
    {
        public string appid;
        public string mch_id;
        public string prepayid;
        public string noncestr;
        public string timestamp;
        public string packageValue;
        public string sign;
    }

}
