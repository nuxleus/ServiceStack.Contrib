using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using ServiceStack.Common;
using ServiceStack.Common.Utils;
using ServiceStack.Common.Web;
using ServiceStack.Service;
using ServiceStack.ServiceClient.Web;
using ServiceStack.ServiceHost;
using ServiceStack.ServiceInterface.ServiceModel;
using ServiceStack.ServiceModel;
using ServiceStack.Text;
using ServiceStack.WebHost.Endpoints;
using ServiceStack.WebHost.Endpoints.Support;

namespace ServiceStack.ServiceInterface.Testing
{
	public abstract class TestBase
	{
		public class TestAppHost : IAppHost
		{
			private readonly TestBase testBase;

			public TestAppHost(TestBase testBase)
			{
				var createInstance = EndpointHostConfig.Instance;

				this.testBase = testBase;
				this.Config = EndpointHost.Config = new EndpointHostConfig {
					ServiceName = GetType().Name,
					ServiceManager = new ServiceManager(true, testBase.ServiceAssemblies),
				};
				this.ContentTypeFilters = new HttpResponseFilter();
				this.RequestFilters = new List<Action<IHttpRequest, IHttpResponse, object>>();
				this.ResponseFilters = new List<Action<IHttpRequest, IHttpResponse, object>>();
			}

			public T TryResolve<T>()
			{
				return this.testBase.Container.TryResolve<T>();
			}

			public IContentTypeFilter ContentTypeFilters { get; set; }

			public List<Action<IHttpRequest, IHttpResponse, object>> RequestFilters { get; set; }

			public List<Action<IHttpRequest, IHttpResponse, object>> ResponseFilters { get; set; }

			public EndpointHostConfig Config { get; set; }
		}

		protected IAppHost AppHost { get; set; }

		protected bool HasConfigured { get; set; }

		protected TestBase(params Assembly[] serviceAssemblies)
			: this(null, serviceAssemblies) {}

		protected TestBase(string serviceClientBaseUri, params Assembly[] serviceAssemblies)
		{
			ServiceClientBaseUri = serviceClientBaseUri;
			ServiceAssemblies = serviceAssemblies;

			var appHost = new TestAppHost(this);
			this.AppHost = appHost;

			EndpointHost.ConfigureHost(this.AppHost, "TestBase", serviceAssemblies);

			EndpointHost.ServiceManager = appHost.Config.ServiceManager;
		}

		protected abstract void Configure(Funq.Container container);

		protected Funq.Container Container
		{
			get { return EndpointHost.ServiceManager.Container; }
		}

		protected IServiceRoutes Routes
		{
			get { return EndpointHost.ServiceManager.ServiceController.Routes; }
		}

		//All integration tests call the Webservices hosted at the following location:
		protected string ServiceClientBaseUri { get; set; }
		protected Assembly[] ServiceAssemblies { get; set; }

		public virtual void OnBeforeTestFixture()
		{
			OnConfigure();
		}

		protected virtual void OnConfigure()
		{
			if (HasConfigured) return;

			HasConfigured = true;
			Configure(Container);
			EndpointHost.AfterInit();
		}

		public virtual void OnBeforeEachTest()
		{
			OnConfigure();
		}

		protected virtual IServiceClient CreateNewServiceClient()
		{
			return new DirectServiceClient(this, EndpointHost.ServiceManager);
		}

		protected virtual IRestClient CreateNewRestClient()
		{
			return new DirectServiceClient(this, EndpointHost.ServiceManager);
		}

		protected virtual IRestClientAsync CreateNewRestClientAsync()
		{
			return new DirectServiceClient(this, EndpointHost.ServiceManager);
		}

		public class DirectServiceClient : IServiceClient, IRestClient
		{
			private readonly TestBase parent;
			ServiceManager ServiceManager { get; set; }

			public DirectServiceClient(TestBase parent, ServiceManager serviceManager)
			{
				this.parent = parent;
				this.ServiceManager = serviceManager;
			}

			public void SendOneWay(object request)
			{
				ServiceManager.Execute(request);
			}

			public TResponse Send<TResponse>(object request)
			{
				var response = ServiceManager.Execute(request);
				return (TResponse)response;
			}

			public TResponse Get<TResponse>(string relativeOrAbsoluteUrl)
			{
				return parent.ExecutePath<TResponse>(HttpMethod.Get, new UrlParts(relativeOrAbsoluteUrl), null);
			}

			public TResponse Delete<TResponse>(string relativeOrAbsoluteUrl)
			{
				return parent.ExecutePath<TResponse>(HttpMethod.Delete, new UrlParts(relativeOrAbsoluteUrl), null);
			}

			public TResponse Post<TResponse>(string relativeOrAbsoluteUrl, object request)
			{
				return parent.ExecutePath<TResponse>(HttpMethod.Post, new UrlParts(relativeOrAbsoluteUrl), request);
			}

			public TResponse Put<TResponse>(string relativeOrAbsoluteUrl, object request)
			{
				return parent.ExecutePath<TResponse>(HttpMethod.Put, new UrlParts(relativeOrAbsoluteUrl), request);
			}

			public TResponse PostFile<TResponse>(string relativeOrAbsoluteUrl, FileInfo fileToUpload, string mimeType)
			{
				throw new NotImplementedException();
			}

			public void SendAsync<TResponse>(object request,
				Action<TResponse> onSuccess, Action<TResponse, Exception> onError)
			{
				try
				{
					var response = (TResponse)ServiceManager.Execute(request);
					onSuccess(response);
				}
				catch (Exception ex)
				{
					HandleException(ex, onError);
				}
			}

			private static void HandleException<TResponse>(Exception exception, Action<TResponse, Exception> onError)
			{
				var response = (TResponse)ReflectionUtils.CreateInstance(typeof(TResponse));
				var hasResponseStatus = response as IHasResponseStatus;
				if (hasResponseStatus != null)
				{
					hasResponseStatus.ResponseStatus = new ResponseStatus {
						ErrorCode = exception.GetType().Name,
						Message = exception.Message,
						StackTrace = exception.StackTrace,
					};
				}
				var webServiceEx = new WebServiceException(exception.Message, exception);
				if (onError != null) onError(response, webServiceEx);
			}

			public void SetCredentials(string userName, string password)
			{
				throw new NotImplementedException();
			}

			public void GetAsync<TResponse>(string relativeOrAbsoluteUrl, Action<TResponse> onSuccess, Action<TResponse, Exception> onError)
			{
				try
				{
					var response = parent.ExecutePath<TResponse>(HttpMethod.Get, new UrlParts(relativeOrAbsoluteUrl), default(TResponse));
					onSuccess(response);
				}
				catch (Exception ex)
				{
					HandleException(ex, onError);
				}
			}

			public void DeleteAsync<TResponse>(string relativeOrAbsoluteUrl, Action<TResponse> onSuccess, Action<TResponse, Exception> onError)
			{
				try
				{
					var response = parent.ExecutePath<TResponse>(HttpMethod.Delete, new UrlParts(relativeOrAbsoluteUrl), default(TResponse));
					onSuccess(response);
				}
				catch (Exception ex)
				{
					HandleException(ex, onError);
				}
			}

			public void PostAsync<TResponse>(string relativeOrAbsoluteUrl, object request, Action<TResponse> onSuccess, Action<TResponse, Exception> onError)
			{
				try
				{
					var response = parent.ExecutePath<TResponse>(HttpMethod.Post, new UrlParts(relativeOrAbsoluteUrl), request);
					onSuccess(response);
				}
				catch (Exception ex)
				{
					HandleException(ex, onError);
				}
			}

			public void PutAsync<TResponse>(string relativeOrAbsoluteUrl, object request, Action<TResponse> onSuccess, Action<TResponse, Exception> onError)
			{
				try
				{
					var response = parent.ExecutePath<TResponse>(HttpMethod.Put, new UrlParts(relativeOrAbsoluteUrl), request);
					onSuccess(response);
				}
				catch (Exception ex)
				{
					HandleException(ex, onError);
				}
			}

			public void Dispose() { }
		}

		public object ExecutePath(string pathInfo)
		{
			return ExecutePath(HttpMethods.Get, pathInfo);
		}

		private class UrlParts
		{
			public UrlParts(string pathInfo)
			{
				this.PathInfo = pathInfo.UrlDecode();
				var qsIndex = pathInfo.IndexOf("?");
				if (qsIndex != -1)
				{
					var qs = pathInfo.Substring(qsIndex + 1);
					this.PathInfo = pathInfo.Substring(0, qsIndex);
					var kvps = qs.Split('&');

					this.QueryString = new Dictionary<string, string>();
					foreach (var kvp in kvps)
					{
						var parts = kvp.Split('=');
						this.QueryString[parts[0]] = parts.Length > 1 ? parts[1] : null;
					}
				}
			}

			public string PathInfo { get; private set; }
			public Dictionary<string, string> QueryString { get; private set; }
		}

		public object ExecutePath(string httpMethod, string pathInfo)
		{
			var urlParts = new UrlParts(pathInfo);
			return ExecutePath(httpMethod, urlParts.PathInfo, urlParts.QueryString, null, null);
		}

		private TResponse ExecutePath<TResponse>(string httpMethod, UrlParts urlParts, object requestDto)
		{
			return (TResponse)ExecutePath(httpMethod, urlParts.PathInfo, urlParts.QueryString, null, requestDto);
		}

		public TResponse ExecutePath<TResponse>(string httpMethod, string pathInfo, object requestDto)
		{
			var urlParts = new UrlParts(pathInfo);
			return (TResponse)ExecutePath(httpMethod, urlParts.PathInfo, urlParts.QueryString, null, requestDto);
		}

		public object ExecutePath<T>(
			string httpMethod,
			string pathInfo,
			Dictionary<string, string> queryString,
			Dictionary<string, string> formData,
			T requestBody)
		{
			var isDefault = Equals(requestBody, default(T));
			var json = !isDefault ? JsonSerializer.SerializeToString(requestBody) : null;
			return ExecutePath(httpMethod, pathInfo, queryString, formData, json);
		}

		public object ExecutePath(
			string httpMethod,
			string pathInfo,
			Dictionary<string, string> queryString,
			Dictionary<string, string> formData,
			string requestBody)
		{
			var httpHandler = GetHandler(httpMethod, pathInfo);

			var contentType = (formData != null && formData.Count > 0)
				? ContentType.FormUrlEncoded
				: requestBody != null ? ContentType.Json : null;

			var httpReq = new MockHttpRequest(
					httpHandler.RequestName, httpMethod, contentType,
					pathInfo,
					queryString.ToNameValueCollection(),
					requestBody == null ? null : new MemoryStream(Encoding.UTF8.GetBytes(requestBody)),
					formData.ToNameValueCollection()
				);

			var request = httpHandler.CreateRequest(httpReq, httpHandler.RequestName);
			var response = httpHandler.GetResponse(httpReq, request);

			var httpRes = response as IHttpResult;
			if (httpRes != null)
			{
				var httpError = httpRes as IHttpError;
				if (httpError != null)
				{
					throw new WebServiceException(httpError.Message) {
						StatusCode = (int)httpError.StatusCode,
						ResponseDto = httpError.Response
					};
				}
				var hasResponseStatus = httpRes.Response as IHasResponseStatus;
				if (hasResponseStatus != null)
				{
					var status = hasResponseStatus.ResponseStatus;
					if (status != null && !status.ErrorCode.IsNullOrEmpty())
					{
						throw new WebServiceException(status.Message) {
							StatusCode = (int)HttpStatusCode.InternalServerError,
							ResponseDto = httpRes.Response,
						};
					}
				}

				return httpRes.Response;
			}

			return response;
		}

		public object GetRequest(string pathInfo)
		{
			var urlParts = new UrlParts(pathInfo);
			return GetRequest(HttpMethods.Get, urlParts.PathInfo, urlParts.QueryString, null, null);
		}

		public object GetRequest(string httpMethod, string pathInfo)
		{
			var urlParts = new UrlParts(pathInfo);
			return GetRequest(httpMethod, urlParts.PathInfo, urlParts.QueryString, null, null);
		}

		public object GetRequest(
				string httpMethod,
				string pathInfo,
				Dictionary<string, string> queryString,
				Dictionary<string, string> formData,
				string requestBody)
		{
			var httpHandler = GetHandler(httpMethod, pathInfo);

			var contentType = (formData != null && formData.Count > 0)
				? ContentType.FormUrlEncoded
				: requestBody != null ? ContentType.Json : null;

			var httpReq = new MockHttpRequest(
					httpHandler.RequestName, httpMethod, contentType,
					pathInfo,
					queryString.ToNameValueCollection(),
					requestBody == null ? null : new MemoryStream(Encoding.UTF8.GetBytes(requestBody)),
					formData.ToNameValueCollection()
				);

			var request = httpHandler.CreateRequest(httpReq, httpHandler.RequestName);
			return request;
		}

		private static EndpointHandlerBase GetHandler(string httpMethod, string pathInfo)
		{
			var httpHandler = ServiceStackHttpHandlerFactory.GetHandlerForPathInfo(httpMethod, pathInfo, null) as EndpointHandlerBase;
			if (httpHandler == null)
				throw new NotSupportedException(pathInfo);
			return httpHandler;
		}
	}

}