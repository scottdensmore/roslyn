﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Roslyn.LanguageServer.Protocol
{
    using Newtonsoft.Json;

    /// <summary>
    /// Utilities to aid work with VS Code LSP Extensions.
    /// </summary>
    internal static class VSCodeInternalExtensionUtilities
    {
        /// <summary>
        /// Adds <see cref="VSExtensionConverter{TBase, TExtension}"/> necessary to deserialize
        /// JSON stream into objects which include VS Code-specific extensions.
        /// </summary>
        /// <remarks>
        /// If <paramref name="serializer"/> is used in parallel to execution of this method,
        /// its access needs to be synchronized with this method call, to guarantee that
        /// <see cref="JsonSerializer.Converters"/> collection is not modified when <paramref name="serializer"/> in use.
        /// </remarks>
        /// <param name="serializer">Instance of <see cref="JsonSerializer"/> which is guaranteed to not work in parallel to this method call.</param>
        public static void AddVSCodeInternalExtensionConverters(this JsonSerializer serializer)
        {
            // Reading the number of converters before we start adding new ones
            var existingConvertersCount = serializer.Converters.Count;

            AddOrReplaceConverter<TextDocumentRegistrationOptions, VSInternalTextDocumentRegistrationOptions>();
            AddOrReplaceConverter<TextDocumentClientCapabilities, VSInternalTextDocumentClientCapabilities>();

            void AddOrReplaceConverter<TBase, TExtension>()
                where TExtension : TBase
            {
                for (var i = 0; i < existingConvertersCount; i++)
                {
                    var existingConverterType = serializer.Converters[i].GetType();
                    if (existingConverterType.IsGenericType &&
                        existingConverterType.GetGenericTypeDefinition() == typeof(VSExtensionConverter<,>) &&
                        existingConverterType.GenericTypeArguments[0] == typeof(TBase))
                    {
                        serializer.Converters.RemoveAt(i);
                        existingConvertersCount--;
                        break;
                    }
                }

                serializer.Converters.Add(new VSExtensionConverter<TBase, TExtension>());
            }
        }
    }
}
