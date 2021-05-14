﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System;
using System.ComponentModel.Composition;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.CodeAnalysis.Editor.InlineErrors
{
    internal sealed class ClassificationTypeDefinitions
    {
        [Export]
        [Name(InlineErrorTag.TagId)]
        [BaseDefinition(PredefinedClassificationTypeNames.FormalLanguage)]
        internal ClassificationTypeDefinition InlineErrors;

        [Export(typeof(EditorFormatDefinition))]
        [Name(InlineErrorTag.TagId)]
        [Order(After = LanguagePriority.NaturalLanguage, Before = LanguagePriority.FormalLanguage)]
        [UserVisible(true)]
        internal sealed class InlineErrorFormatDefinition : EditorFormatDefinition
        {
            [ImportingConstructor]
            [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
            public InlineErrorFormatDefinition()
            {
                this.DisplayName = EditorFeaturesResources.Inline_Errors;
            }
        }
    }
}
