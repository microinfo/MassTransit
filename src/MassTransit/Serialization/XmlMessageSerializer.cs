// Copyright 2007-2011 Chris Patterson, Dru Sellers, Travis Smith, et. al.
//  
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use 
// this file except in compliance with the License. You may obtain a copy of the 
// License at 
// 
//     http://www.apache.org/licenses/LICENSE-2.0 
// 
// Unless required by applicable law or agreed to in writing, software distributed 
// under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR 
// CONDITIONS OF ANY KIND, either express or implied. See the License for the 
// specific language governing permissions and limitations under the License.
namespace MassTransit.Serialization
{
	using System;
	using System.Collections.Generic;
	using System.Globalization;
	using System.IO;
	using System.Text;
	using System.Xml;
	using System.Xml.Linq;
	using Context;
	using Custom;
	using Newtonsoft.Json;
	using Newtonsoft.Json.Converters;
	using Newtonsoft.Json.Linq;

	public class XmlMessageSerializer :
		IMessageSerializer
	{
		const string ContentTypeHeaderValue = "application/vnd.masstransit+xml";


		[ThreadStatic]
		static JsonSerializer _deserializer;

		[ThreadStatic]
		static JsonSerializer _serializer;

		[ThreadStatic]
		static JsonSerializer _xmlSerializer;

		public string ContentType
		{
			get { return ContentTypeHeaderValue; }
		}

		public void Serialize<T>(Stream output, ISendContext<T> context)
			where T : class
		{
			context.SetContentType(ContentTypeHeaderValue);

			Envelope envelope = Envelope.Create(context);

			var json = new StringBuilder(1024);

			using(var stringWriter = new StringWriter(json, CultureInfo.InvariantCulture))
			using (var jsonWriter = new JsonTextWriter(stringWriter))
			{
				jsonWriter.Formatting = Newtonsoft.Json.Formatting.None;

				Serializer.Serialize(jsonWriter, envelope);

				jsonWriter.Flush();
				stringWriter.Flush();
			}

			using(var stringReader = new StringReader(json.ToString()))
			using (var jsonReader = new JsonTextReader(stringReader))
			{
				var document = (XDocument) XmlSerializer.Deserialize(jsonReader, typeof (XDocument));

				using (var nonClosingStream = new NonClosingStream(output))
				using (var streamWriter = new StreamWriter(nonClosingStream))
				using (var xmlWriter = new XmlTextWriter(streamWriter))
				{
					document.WriteTo(xmlWriter);
				}
			}
		}

		public void Deserialize(IReceiveContext context)
		{
			XDocument document;
			using (var nonClosingStream = new NonClosingStream(context.BodyStream))
			using (var xmlReader = new XmlTextReader(nonClosingStream))
				document = XDocument.Load(xmlReader);

			var json = new StringBuilder(1024);

			using(var stringWriter = new StringWriter(json, CultureInfo.InvariantCulture))
			using (var jsonWriter = new JsonTextWriter(stringWriter))
			{
				jsonWriter.Formatting = Newtonsoft.Json.Formatting.None;

				XmlSerializer.Serialize(jsonWriter, document.Root);
			}

			Envelope result;
			using(var stringReader = new StringReader(json.ToString()))
			using (var jsonReader = new JsonTextReader(stringReader))
			{
				result = Deserializer.Deserialize<Envelope>(jsonReader);
			}

			context.SetUsingEnvelope(result);
			context.SetMessageTypeConverter(new JsonMessageTypeConverter(Deserializer, result.Message as JToken,
				result.MessageType));
		}

		static JsonSerializer Serializer
		{
			get
			{
				return _serializer ?? (_serializer = JsonSerializer.Create(new JsonSerializerSettings
				{
					NullValueHandling = NullValueHandling.Ignore,
					DefaultValueHandling = DefaultValueHandling.Ignore,
					MissingMemberHandling = MissingMemberHandling.Ignore,
					ObjectCreationHandling = ObjectCreationHandling.Auto,
					ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
					ContractResolver = new JsonContractResolver(),
				}));
			}
		}

		static JsonSerializer XmlSerializer
		{
			get
			{
				return _xmlSerializer ?? (_xmlSerializer = JsonSerializer.Create(new JsonSerializerSettings
					{
						NullValueHandling = NullValueHandling.Ignore,
						DefaultValueHandling = DefaultValueHandling.Ignore,
						MissingMemberHandling = MissingMemberHandling.Ignore,
						ObjectCreationHandling = ObjectCreationHandling.Auto,
						ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
						Converters = new List<JsonConverter>(new[]
							{
								new XmlNodeConverter
									{
										DeserializeRootElementName = "envelope",
										WriteArrayAttribute = false,
										OmitRootObject = true,
									}
							})
					}));
			}
		}

		static JsonSerializer Deserializer
		{
			get
			{
				return _deserializer ?? (_deserializer = JsonSerializer.Create(new JsonSerializerSettings
				{
					NullValueHandling = NullValueHandling.Ignore,
					DefaultValueHandling = DefaultValueHandling.Ignore,
					MissingMemberHandling = MissingMemberHandling.Ignore,
					ObjectCreationHandling = ObjectCreationHandling.Auto,
					ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
					ContractResolver = new JsonContractResolver(),
					Converters = new List<JsonConverter>(new JsonConverter[]
							{
								new ListJsonConverter(),
								new InterfaceProxyConverter(),
							})
				}));
			}
		}
	}
}