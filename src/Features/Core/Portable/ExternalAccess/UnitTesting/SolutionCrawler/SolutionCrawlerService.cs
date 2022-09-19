﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Composition;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Options;

namespace Microsoft.CodeAnalysis.ExternalAccess.UnitTesting.SolutionCrawler
{
    internal partial class UnitTestingSolutionCrawlerRegistrationService : IUnitTestingSolutionCrawlerRegistrationService
    {
        internal static readonly Option2<bool> EnableSolutionCrawler = new("InternalSolutionCrawlerOptions", "Solution Crawler", defaultValue: true,
            storageLocation: new LocalUserProfileStorageLocation(@"Roslyn\Internal\SolutionCrawler\Solution Crawler"));

        /// <summary>
        /// nested class of <see cref="UnitTestingSolutionCrawlerRegistrationService"/> since it is tightly coupled with it.
        /// 
        /// <see cref="IUnitTestingSolutionCrawlerService"/> is implemented by this class since WorkspaceService doesn't allow a class to implement
        /// more than one <see cref="IWorkspaceService"/>.
        /// </summary>
        [ExportWorkspaceService(typeof(IUnitTestingSolutionCrawlerService), ServiceLayer.Default), Shared]
        internal class SolutionCrawlerService : IUnitTestingSolutionCrawlerService
        {
            [ImportingConstructor]
            [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
            public SolutionCrawlerService()
            {
            }

            public void Reanalyze(Workspace workspace, IUnitTestingIncrementalAnalyzer analyzer, IEnumerable<ProjectId>? projectIds = null, IEnumerable<DocumentId>? documentIds = null, bool highPriority = false)
            {
                // if solution crawler doesn't exist for the given workspace. don't do anything
                if (workspace.Services.GetService<IUnitTestingSolutionCrawlerRegistrationService>() is UnitTestingSolutionCrawlerRegistrationService registration)
                {
                    registration.Reanalyze(workspace, analyzer, projectIds, documentIds, highPriority);
                }
            }

            public IUnitTestingSolutionCrawlerProgressReporter GetProgressReporter(Workspace workspace)
            {
                // if solution crawler doesn't exist for the given workspace, return null reporter
                if (workspace.Services.GetService<IUnitTestingSolutionCrawlerRegistrationService>() is UnitTestingSolutionCrawlerRegistrationService registration)
                {
                    // currently we have only 1 global reporter that are shared by all workspaces.
                    return registration._progressReporter;
                }

                return NullReporter.Instance;
            }
        }
    }
}
