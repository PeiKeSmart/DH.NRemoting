﻿using System.Web;
using NewLife;
using NewLife.Collections;
using NewLife.Data;
using NewLife.Messaging;
using NewLife.Reflection;
using NewLife.Serialization;

namespace NewLife.Remoting.Http;

/// <summary>Http编码器</summary>
public class HttpEncoder : EncoderBase, IEncoder
{
    #region 属性
    /// <summary>Json主机。提供序列化能力</summary>
    public IJsonHost JsonHost { get; set; } = JsonHelper.Default;

    /// <summary>是否使用Http状态。默认false，使用json包装响应码</summary>
    public Boolean UseHttpStatus { get; set; }
    #endregion

    /// <summary>编码</summary>
    /// <param name="action"></param>
    /// <param name="code"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    public virtual IPacket? Encode(String action, Int32 code, Object? value)
    {
        //if (value == null) return null;

        if (value is IPacket pk) return pk;
        if (value is IAccessor acc) return acc.ToPacket();

        // 不支持序列化异常
        if (value is Exception ex) value = ex.GetTrue().Message;

        if (value == null) return null;

        var json = UseHttpStatus ?
            JsonHost.Write(value, false, false, false) :
            JsonHost.Write(new { action, code, data = value }, false, true, false);
        WriteLog("{0}=>{1}", action, json);

        return (ArrayPacket)json.GetBytes();
    }

    /// <summary>解码参数</summary>
    /// <param name="action"></param>
    /// <param name="data"></param>
    /// <param name="msg"></param>
    /// <returns></returns>
    public virtual Object? DecodeParameters(String action, IPacket? data, IMessage msg)
    {
        if (data == null || data.Total == 0) return null;

        /*
         * 数据内容解析需要根据http数据类型来判定使用什么格式处理
         * **/

        var str = data.ToStr();
        WriteLog("{0}<={1}", action, str);
        if (str.IsNullOrEmpty()) return null;

        var ctype = new String[0];
        if (msg is HttpMessage hmsg && str[0] == '{')
            if (hmsg.ParseHeaders()) ctype = (hmsg.Headers["Content-type"] + "").Split(';');

        if (ctype.Contains("application/json"))
        {
            // 返回类型可能是列表而不是字典
            var obj = JsonHost.Parse(str);

            if (obj is not IDictionary<String, Object?> dic)
                return obj;

            var rs = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in dic)
                if (item.Value is String str2)
                    rs[item.Key] = HttpUtility.UrlDecode(str2);
                else
                    rs[item.Key] = item.Value;
            return rs;
        }
        else
        {
            var dic = str.SplitAsDictionary("=", "&");
            var rs = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in dic)
                rs[item.Key] = HttpUtility.UrlDecode(item.Value);
            return rs;
        }
    }

    /// <summary>解码结果</summary>
    /// <param name="action"></param>
    /// <param name="data"></param>
    /// <param name="msg">消息</param>
    /// <param name="returnType">返回类型</param>
    /// <returns></returns>
    public virtual Object? DecodeResult(String action, IPacket data, IMessage msg, Type returnType)
    {
        var json = data?.ToStr();
        WriteLog("{0}<={1}", action, json);

        // 支持基础类型
        if (returnType != null && returnType.GetTypeCode() != TypeCode.Object) return json.ChangeType(returnType);

        if (json.IsNullOrEmpty()) return null;
        if (returnType == null || returnType == typeof(String)) return json;

        // 返回类型可能是列表而不是字典
        var rs = JsonHost.Parse(json);
        if (rs == null) return null;
        if (returnType == typeof(Object)) return rs;

        return Convert(rs, returnType);
    }

    /// <summary>转换为目标类型</summary>
    /// <param name="obj"></param>
    /// <param name="targetType"></param>
    /// <returns></returns>
    public virtual Object? Convert(Object obj, Type targetType) => JsonHost.Convert(obj, targetType);

    #region 编码/解码
    /// <summary>创建请求</summary>
    /// <param name="action"></param>
    /// <param name="args"></param>
    /// <returns></returns>
    public virtual IMessage CreateRequest(String action, Object? args)
    {
        // 请求方法 GET / HTTP/1.1
        var req = new HttpMessage();
        var sb = Pool.StringBuilder.Get();
        sb.Append("GET ");
        sb.Append(action);

        // 准备参数，二进制优先
        IPacket? pk = null;
        if (args != null)
            if (args is IPacket pk2)
                pk = pk2;
            else if (args is IAccessor acc)
                pk = acc.ToPacket();
            else if (args is Byte[] buf)
                pk = new ArrayPacket(buf);
            else if (args is String str2)
                pk = (ArrayPacket)str2.GetBytes();
            else if (args is DateTime dt)
                pk = (ArrayPacket)dt.ToFullString().GetBytes();
            else if (args.GetType().GetTypeCode() != TypeCode.Object)
                pk = (ArrayPacket)(args + "").GetBytes();
            else
            {
                // url参数
                sb.Append('?');
                if (args.GetType().GetTypeCode() != TypeCode.Object)
                    sb.Append(args);
                else
                    foreach (var item in args.ToDictionary())
                        sb.AppendFormat("{0}={1}", item.Key, item.Value);
            }
        sb.AppendLine(" HTTP/1.1");

        if (pk != null && pk.Total > 0)
        {
            sb.AppendFormat("Content-Length:{0}\r\n", pk.Total);
            sb.AppendLine("Content-Type:application/json");
        }
        sb.Append("Connection:keep-alive");

        req.Header = (ArrayPacket)sb.Return(true).GetBytes();

        return req;
    }

    /// <summary>创建响应</summary>
    /// <param name="msg"></param>
    /// <param name="action"></param>
    /// <param name="code"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    public virtual IMessage CreateResponse(IMessage msg, String action, Int32 code, Object? value)
    {
        if (code <= 0 && UseHttpStatus) code = 200;

        // 编码响应数据包，二进制优先
        var pk = Encode(action, code, value);

        // 构造响应消息
        var rs = new HttpMessage
        {
            Payload = pk
        };
        if (code >= 500) rs.Error = true;

        // HTTP/1.1 502 Bad Gateway
        var sb = Pool.StringBuilder.Get();
        sb.Append("HTTP/1.1 ");

        if (UseHttpStatus)
        {
            sb.Append(code);
            if (code < 500)
                sb.AppendLine(" OK");
            else
                sb.AppendLine(" Error");
        }
        else
            sb.AppendLine("200 OK");

        sb.AppendFormat("Content-Length:{0}\r\n", pk?.Total ?? 0);
        sb.AppendLine("Content-Type:application/json");
        sb.Append("Connection:keep-alive");

        rs.Header = (ArrayPacket)sb.Return(true).GetBytes();

        return rs;
    }

    private static Byte[] NewLife => "\r\n"u8.ToArray();
    /// <summary>解码 请求/响应</summary>
    /// <param name="msg">消息</param>
    /// <returns>请求响应报文</returns>
    public override ApiMessage? Decode(IMessage msg)
    {
        if (msg is not HttpMessage http || http.Header == null) return null;

        // 分析请求方法 GET / HTTP/1.1
        var span = http.Header.GetSpan();
        var p = span.IndexOf(NewLife);
        if (p <= 0) return null;

        var line = span[..p].ToStr();

        var ss = line.Split(' ');
        if (ss.Length < 3) return null;

        var message = new ApiMessage();

        // 第二段是地址
        var uri = new Uri(ss[1], UriKind.RelativeOrAbsolute);
        if (!uri.IsAbsoluteUri)
        {
            var url = ss[1];
            p = url.IndexOf('?');
            if (p > 0)
            {
                message.Action = url.Substring(1, p - 1);
                message.Data = (ArrayPacket)url.Substring(p + 1).GetBytes();
            }
            else
            {
                message.Action = url.Substring(1);
                message.Data = http.Payload;
            }
        }
        else
        {
            if (!uri.Query.IsNullOrEmpty())
            {
                message.Action = uri.AbsolutePath;
                message.Data = (ArrayPacket)uri.Query.GetBytes();
            }
            else
            {
                message.Action = uri.AbsolutePath;
                message.Data = http.Payload;
            }
            if (message.Action.Length > 1) message.Action = message.Action.Substring(1);
        }

        return message;
    }
    #endregion
}