﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeStyle;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Simplification;

namespace Microsoft.CodeAnalysis.CodeQuality
{
    internal abstract class AbstractCodeQualityDiagnosticAnalyzer : DiagnosticAnalyzer, IBuiltInAnalyzer
    {
        private readonly GeneratedCodeAnalysisFlags _generatedCodeAnalysisFlags;

        protected AbstractCodeQualityDiagnosticAnalyzer(
            ImmutableArray<DiagnosticDescriptor> descriptors,
            GeneratedCodeAnalysisFlags generatedCodeAnalysisFlags)
        {
            SupportedDiagnostics = descriptors;
            _generatedCodeAnalysisFlags = generatedCodeAnalysisFlags;
        }

        public CodeActionRequestPriority RequestPriority => CodeActionRequestPriority.Normal;
        public sealed override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }

        public sealed override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(_generatedCodeAnalysisFlags);
            context.EnableConcurrentExecution();

            InitializeWorker(context);
        }

        protected abstract void InitializeWorker(AnalysisContext context);

        public abstract DiagnosticAnalyzerCategory GetAnalyzerCategory();

        public bool OpenFileOnly(SimplifierOptions? options)
            => false;

        protected static DiagnosticDescriptor CreateDescriptor(
            string id,
            EnforceOnBuild enforceOnBuild,
            LocalizableString title,
            LocalizableString messageFormat,
            bool isUnnecessary,
            bool isEnabledByDefault = true,
            bool isConfigurable = true,
            LocalizableString? description = null) => new(
                    id, title, messageFormat,
                    DiagnosticCategory.CodeQuality,
                    DiagnosticSeverity.Info,
                    isEnabledByDefault,
                    description,
                    helpLinkUri: DiagnosticHelper.GetHelpLinkForDiagnosticId(id),
                    customTags: DiagnosticCustomTags.Create(isUnnecessary, isConfigurable, enforceOnBuild));
    }
}
