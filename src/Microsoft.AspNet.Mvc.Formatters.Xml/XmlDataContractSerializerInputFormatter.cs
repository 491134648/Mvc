// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.AspNet.Mvc.Formatters.Xml;
using Microsoft.AspNet.Mvc.Formatters.Xml.Internal;

namespace Microsoft.AspNet.Mvc.Formatters
{
    /// <summary>
    /// This class handles deserialization of input XML data
    /// to strongly-typed objects using <see cref="DataContractSerializer"/>.
    /// </summary>
    public class XmlDataContractSerializerInputFormatter : InputFormatter
    {
        private DataContractSerializerSettings _serializerSettings;
        private ConcurrentDictionary<Type, object> _serializerCache = new ConcurrentDictionary<Type, object>();
        private readonly XmlDictionaryReaderQuotas _readerQuotas = FormattingUtilities.GetDefaultXmlReaderQuotas();

        /// <summary>
        /// Initializes a new instance of DataContractSerializerInputFormatter
        /// </summary>
        public XmlDataContractSerializerInputFormatter()
        {
            SupportedEncodings.Add(UTF8EncodingWithoutBOM);
            SupportedEncodings.Add(UTF16EncodingLittleEndian);

            SupportedMediaTypes.Add(MediaTypeHeaderValues.ApplicationXml);
            SupportedMediaTypes.Add(MediaTypeHeaderValues.TextXml);

            _serializerSettings = new DataContractSerializerSettings();

            WrapperProviderFactories = new List<IWrapperProviderFactory>();
            WrapperProviderFactories.Add(new SerializableErrorWrapperProviderFactory());
        }

        /// <summary>
        /// Gets the list of <see cref="IWrapperProviderFactory"/> to
        /// provide the wrapping type for de-serialization.
        /// </summary>
        public IList<IWrapperProviderFactory> WrapperProviderFactories { get; }

        /// <summary>
        /// Indicates the acceptable input XML depth.
        /// </summary>
        public int MaxDepth
        {
            get { return _readerQuotas.MaxDepth; }
            set { _readerQuotas.MaxDepth = value; }
        }

        /// <summary>
        /// The quotas include - DefaultMaxDepth, DefaultMaxStringContentLength, DefaultMaxArrayLength,
        /// DefaultMaxBytesPerRead, DefaultMaxNameTableCharCount
        /// </summary>
        public XmlDictionaryReaderQuotas XmlDictionaryReaderQuotas
        {
            get { return _readerQuotas; }
        }

        /// <summary>
        /// Gets or sets the <see cref="DataContractSerializerSettings"/> used to configure the
        /// <see cref="DataContractSerializer"/>.
        /// </summary>
        public DataContractSerializerSettings SerializerSettings
        {
            get { return _serializerSettings; }
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                _serializerSettings = value;
            }
        }

        /// <inheritdoc />
        public override Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context)
        {
            var effectiveEncoding = SelectCharacterEncoding(context);
            if (effectiveEncoding == null)
            {
                return InputFormatterResult.FailureAsync();
            }

            var type = GetSerializableType(context.ModelType);
            var serializer = GetCachedSerializer(type);

            object deserializedObject;
            var request = context.HttpContext.Request;
            using (var reader = context.ReaderFactory(request.Body, effectiveEncoding))
            {
                using (var xmlTextReader = XmlReader.Create(reader))
                {
                    using (var xmlReader = XmlDictionaryReader.CreateDictionaryReader(xmlTextReader))
                    {
                        _readerQuotas.CopyTo(xmlReader.Quotas);
                        deserializedObject = serializer.ReadObject(xmlReader);
                    }
                }
            }

            // Unwrap only if the original type was wrapped.
            if (type != context.ModelType)
            {
                var unwrappable = deserializedObject as IUnwrappable;
                if (unwrappable != null)
                {
                    deserializedObject = unwrappable.Unwrap(declaredType: context.ModelType);
                }
            }

            return InputFormatterResult.SuccessAsync(deserializedObject);
        }

        /// <inheritdoc />
        protected override bool CanReadType(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            return GetCachedSerializer(GetSerializableType(type)) != null;
        }

        /// <summary>
        /// Gets the type to which the XML will be deserialized.
        /// </summary>
        /// <param name="declaredType">The declared type.</param>
        /// <returns>The type to which the XML will be deserialized.</returns>
        protected virtual Type GetSerializableType(Type declaredType)
        {
            if (declaredType == null)
            {
                throw new ArgumentNullException(nameof(declaredType));
            }

            var wrapperProvider = WrapperProviderFactories.GetWrapperProvider(
                new WrapperProviderContext(declaredType, isSerialization: false));

            return wrapperProvider?.WrappingType ?? declaredType;
        }

        /// <summary>
        /// Called during deserialization to get the <see cref="DataContractSerializer"/>.
        /// </summary>
        /// <param name="type">The type of object for which the serializer should be created.</param>
        /// <returns>The <see cref="DataContractSerializer"/> used during deserialization.</returns>
        protected virtual DataContractSerializer CreateSerializer(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            try
            {
                // If the serializer does not support this type it will throw an exception.
                return new DataContractSerializer(type, _serializerSettings);
            }
            catch (Exception)
            {
                // We do not surface the caught exception because if CanRead returns
                // false, then this Formatter is not picked up at all.
                return null;
            }
        }

        /// <summary>
        /// Gets the cached serializer or creates and caches the serializer for the given type.
        /// </summary>
        /// <returns>The <see cref="DataContractSerializer"/> instance.</returns>
        protected virtual DataContractSerializer GetCachedSerializer(Type type)
        {
            object serializer;
            if (!_serializerCache.TryGetValue(type, out serializer))
            {
                serializer = CreateSerializer(type);
                if (serializer != null)
                {
                    _serializerCache.TryAdd(type, serializer);
                }
            }

            return (DataContractSerializer)serializer;
        }
    }
}