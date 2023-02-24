﻿using Rio.Common.Helpers;
using Rio.Extensions;
using System.Net;
using System.Text;

namespace Rio.Common.Http;

public class HttpResponse
{
    public HttpStatusCode StatusCode { get;set; }

    public byte[]? ResponseBytes { get; set; }

    public IDictionary<string, string?> Headers { get; set; } = new Dictionary<string, string?>();
}

public class WebRequestHttpRequester:IHttpRequester
{
    public static IHttpRequester New() => new WebRequestHttpRequester();

    #region private fields

    private HttpWebRequest _request = null!;
    private byte[]? _requestDataBytes;
    private string _requestUrl = null!;

    #endregion private fields

    #region ctor

    public WebRequestHttpRequester()
    {

    }

    public WebRequestHttpRequester(string requestUrl):this(requestUrl,HttpMethod.Get)
    {

    }

    public WebRequestHttpRequester(string requestUrl,IDictionary<string,string>? queryDictionary,HttpMethod method)
    {
        _requestUrl = $"{requestUrl}{(requestUrl.Contains("?") ? "&" : "?")}{queryDictionary.ToQueryString()}";
        _request = WebRequest.CreateHttp(requestUrl);
        _request.UserAgent = HttpHelper.GetUserAgent();

        _request.Method = method.Method;
    }

    public WebRequestHttpRequester(string requestUrl,HttpMethod method)
    {
        _requestUrl = requestUrl;
        _request=WebRequest.CreateHttp(requestUrl);
        _request.UserAgent = HttpHelper.GetUserAgent();

        _request.Method = method.Method;
    }

    #endregion ctor

    public IHttpRequester WithUrl(string url)
    {
        _requestUrl= url;
        _request = WebRequest.CreateHttp(url);
        return this;
    }

    public IHttpRequester WithMethod(HttpMethod method)
    {
        _request.Method = method.Method;
        return this;
    }

    public IHttpRequester WithHeaders(IEnumerable<KeyValuePair<string,string?>> customHeaders)
    {
        Guard.NotNull(customHeaders, nameof(customHeaders));
        foreach (var header in customHeaders)
        {
            if(header.Value is null)
            {
                _request.Headers.Remove(header.Key);
                continue;
            }
            if(header.Key.EqualsIgnoreCase("REFERER"))
            {
                _request.Referer = header.Value;
                continue;
            }
            if(header.Key.EqualsIgnoreCase("User-Agent"))
            {
                _request.UserAgent = header.Value;
            }
            if(header.Key.EqualsIgnoreCase("COOKIE"))
            {
                var cookieCollection = new CookieCollection();
                var host = new Uri(_requestUrl).Host;
                header.Value.Split(";").ForEach(c => cookieCollection.Add(new Cookie(c.Split("=")[0].Trim(), c.Split("=")[1].Trim(), "/", host)));
                this.WithCookie(cookieCollection);
                continue;
            }
            _request.Headers.Add(header.Key, header.Value);
        }
        return this;
    }

    private static bool IsNeedRequestStream(string requestMethod)
        => requestMethod.EqualsIgnoreCase("POST") || requestMethod.EqualsIgnoreCase("PUT") || requestMethod.EqualsIgnoreCase("PATCH");

    public IHttpRequester WithUserAgent(bool isMobile)
    {
        _request.UserAgent=HttpHelper.GetUserAgent(isMobile);
        return this;
    }

    public IHttpRequester WithUserAgent(string userAgent)
    {
        _request.UserAgent = userAgent;
        return this;
    }

    public IHttpRequester WithReferer(string referer)
    {
        _request.Referer = referer;
        return this;
    }

    public IHttpRequester WithProxy(IWebProxy proxy)
    {
        Guard.NotNull(proxy,nameof(proxy));
        _request.Proxy = proxy;
        return this;
    }

    public IHttpRequester WithCookie(string? url,Cookie cookie)
    {
        if(null==_request.CookieContainer)
        {
            _request.CookieContainer=new CookieContainer();
        }
        if(string.IsNullOrWhiteSpace(url))
        {
            _request.CookieContainer.Add(cookie);
        }
        else
        {
            _request.CookieContainer.Add(new Uri(url!), cookie);
        }
        return this;
    }

    public IHttpRequester WithCookie(string? url,CookieCollection cookies)
    {
        if(null==_request.CookieContainer)
        {
            _request.CookieContainer = new CookieContainer();
        }
        if(string.IsNullOrWhiteSpace(url))
        {
            _request.CookieContainer.Add(cookies);
        }
        else
        {
            _request.CookieContainer.Add(new Uri(url), cookies);
        }
        return this;
    }

    public IHttpRequester WithParameters(byte[] requestBytes)
        => WithParameters(requestBytes, null);

    public IHttpRequester WithParameters(byte[] requestBytes,string? contentType)
    {
        Guard.NotNull(requestBytes, nameof(requestBytes));
        _requestDataBytes = requestBytes;
        if(string.IsNullOrWhiteSpace(contentType))
        {
            contentType = "application/json;chartset=utf-8";
        }
        _request.ContentType = contentType;
        return this;
    }

    public IHttpRequester WithFile(string filePath, string fileKey = "file",
        IEnumerable<KeyValuePair<string, string>>? formFields = null)
        => WithFile(Path.GetFileName(filePath), File.ReadAllBytes(filePath), fileKey, formFields);

    public IHttpRequester WithFile(string fileName,byte[] fileBytes,string fileKey="file",
        IEnumerable<KeyValuePair<string,string>>? formFields=null)
    {
        var boundary = $"----------------------------{DateTime.Now.Ticks:X}";

        var boundaryBytes = Encoding.ASCII.GetBytes($"\r\n--{boundary}\r\n");
        var endBoundaryBytes = Encoding.ASCII.GetBytes($"\r\n--{boundary}--");

        using (var memStream = new MemoryStream())
        {
            if(formFields!=null)
            {
                foreach (var pair in formFields)
                {
                    memStream.Write(Encoding.UTF8.GetBytes(string.Format(HttpHelper.FormDataFormat, pair.Key, pair.Value, boundary)));
                }
            }
            memStream.Write(boundaryBytes);
            memStream.Write(Encoding.UTF8.GetBytes(string.Format(HttpHelper.FileHeaderFormat, fileKey, fileName)));
            memStream.Write(fileBytes);
            memStream.Write(endBoundaryBytes);
            _requestDataBytes = memStream.ToArray();
        }

        _request.ContentType = $"multipart/form-data: boundary={boundary}";
        _request.KeepAlive = true;

        return this;
    }

    public IHttpRequester WithFiles(IEnumerable<string> filePaths,
        IEnumerable<KeyValuePair<string, string>>? formFields = null)
        => WithFiles(
            filePaths.Select(_ => new KeyValuePair<string, byte[]>(
                Path.GetFileName(_),
                File.ReadAllBytes(_))),
            formFields);

    public IHttpRequester WithFiles(IEnumerable<KeyValuePair<string,byte[]>> files,
        IEnumerable<KeyValuePair<string,string>>? formFields=null)
    {
        var boundary = $"----------------------------{DateTime.Now.Ticks:X}";

        var boundaryBytes = Encoding.ASCII.GetBytes($"\r\n--{boundary}\r\n");
        var endBoundaryBytes = Encoding.ASCII.GetBytes($"\r\n--{boundary}--");

        using (var memStream=new MemoryStream())
        {
            if (formFields != null)
            {
                foreach (var pair in formFields)
                {
                    memStream.Write(Encoding.UTF8.GetBytes(string.Format(HttpHelper.FormDataFormat, pair.Key, pair.Value, boundary)));
                }
            }

            foreach (var file in files)
            {
                memStream.Write(boundaryBytes);
                memStream.Write(Encoding.UTF8.GetBytes(string.Format(HttpHelper.FileHeaderFormat, Path.GetFileNameWithoutExtension(file.Key), file.Key)));
                memStream.Write(file.Value);
            }
            memStream.Write(endBoundaryBytes);

            _requestDataBytes = memStream.ToArray();

            _request.ContentType = $"multipart/form-data: boundary={boundary}";
            _request.KeepAlive = true;
        }

        return this;
    }

    public void BuildRequest()
    {
        if (IsNeedRequestStream(_request.Method)
            && _requestDataBytes != null
            && _requestDataBytes.Length>0)
        {
            _request.ContentLength = _requestDataBytes.Length;
            var requestStream = _request.GetRequestStream();
            requestStream.Write(_requestDataBytes);
        }
    }

    public async Task BuildRequestAsync()
    {
        if (IsNeedRequestStream(_request.Method)
            && _requestDataBytes != null
            && _requestDataBytes.Length > 0)
        {
            _request.ContentLength = _requestDataBytes.Length;
            var requestStream = await _request.GetRequestStreamAsync();
            await requestStream.WriteAsync(_requestDataBytes);
        }
    }

    public HttpResponse ExecuteForResponse()
    {
        BuildRequest();
        var response = (HttpWebResponse)_request.GetResponse();
        return new HttpResponse
        {
            Headers = response.Headers.ToDictionary(),
            StatusCode = response.StatusCode,
            ResponseBytes = response.ReadAllBytes()
        };
    }

    public async Task<HttpResponse> ExecuteForResponseAsync()
    {
        await BuildRequestAsync();
        var response = (HttpWebResponse)(await _request.GetResponseAsync());
        var responseBytes = await response.ReadAllBytesAsync();
        return new HttpResponse
        {
            Headers = response.Headers.ToDictionary(),
            StatusCode = response.StatusCode,
            ResponseBytes = responseBytes
        };
    }

    public byte[] ExecuteBytes()
    {
        BuildRequest();
        return _request.GetResponseBytesSafe();
    }

    public async Task<byte[]> ExecuteBytesAsync()
    {
        await BuildRequestAsync();
        return await _request.GetResponseBytesSafeAsync();
    }

    public async Task<string> ExecuteAsync()
    {
        await BuildRequestAsync();
        return await _request.GetResponseStringSafeAsync();
    }
}


public class HttpClientHttpRequester:IHttpRequester
{
    private HttpClient _client = null!;

    private readonly HttpRequestMessage _request = new();

    private CookieContainer? _cookieContainer;
    private IWebProxy? _proxy;

    private void BuilHttpClient()
    {
        var handler = new HttpClientHandler();

        if(_proxy==null)
        {
            handler.UseProxy = false;
            handler.Proxy = null;
        }
        else
        {
            handler.UseProxy = true;
            handler.Proxy = _proxy;
        }

        if(_cookieContainer==null)
        {
            handler.UseCookies = false;
            handler.CookieContainer = _cookieContainer;
        }
        else
        {
            handler.UseCookies = true;
            handler.CookieContainer = _cookieContainer;
        }
        _client = new HttpClient(handler);
    }

    public IHttpRequester WithCookie(string? url,Cookie cookie)
    {
        _cookieContainer ??= new CookieContainer();

        if(string.IsNullOrEmpty(url))
        {
            _cookieContainer.Add(cookie);
        }
        else
        {
            _cookieContainer.Add(new Uri(url!), cookie);
        }

        return this;
    }

    public IHttpRequester WithCookie(string? url,CookieCollection cookies)
    {
        _cookieContainer??=new CookieContainer();

        if(string.IsNullOrEmpty(url))
        {
            _cookieContainer.Add(cookies);
        }
        else
        {
            _cookieContainer.Add(new Uri(url!), cookies);
        }

        return this;
    }

    public IHttpRequester WithProxy(IWebProxy proxy)
    {
        _proxy = proxy;
        return this;
    }


}
