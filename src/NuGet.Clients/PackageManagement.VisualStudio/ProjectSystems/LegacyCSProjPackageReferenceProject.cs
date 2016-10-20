﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using NuGet.Commands;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.ProjectManagement;
using NuGet.ProjectManagement.Projects;
using NuGet.ProjectModel;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using VSLangProj;
using VSLangProj150;
using Shell = Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace NuGet.PackageManagement.VisualStudio
{
/// <summary>
/// An implementation of <see cref="NuGetProject"/> that interfaces with VS project APIs to coordinate
/// packages in a legacy CSProj with package references.
/// </summary>
    public class LegacyCSProjPackageReferenceProject : CpsPackageReferenceProjectBase, IDependencyGraphProject
    {
        private const string _assetsFileName = "project.assets.json";
        private const string _includeAssets = "IncludeAssets";
        private const string _excludeAssets = "ExcludeAssets";
        private const string _privateAssets = "PrivateAssets";

        private static Array _desiredPackageReferenceMetadata;

        private readonly IEnvDTEProjectAdapter _project;

        static LegacyCSProjPackageReferenceProject()
        {
            _desiredPackageReferenceMetadata = Array.CreateInstance(typeof(string), 3);
            _desiredPackageReferenceMetadata.SetValue(_includeAssets, 0);
            _desiredPackageReferenceMetadata.SetValue(_excludeAssets, 1);
            _desiredPackageReferenceMetadata.SetValue(_privateAssets, 2);
        }

        public LegacyCSProjPackageReferenceProject(
            IEnvDTEProjectAdapter project)
        {
            if (project == null)
            {
                throw new ArgumentNullException(nameof(project));
            }

            _project = project;

            PathToAssetsFile = Path.Combine(_project.GetBaseIntermediatePath().Result ?? "", _assetsFileName);

            InternalMetadata.Add(NuGetProjectMetadataKeys.Name, _project.Name);
            InternalMetadata.Add(NuGetProjectMetadataKeys.UniqueName, _project.UniqueName);
            InternalMetadata.Add(NuGetProjectMetadataKeys.FullPath, _project.ProjectFullPath);
        }

        #region IDependencyGraphProject

        /// <summary>
        /// Making this timestamp as the current time means that a restore with this project in the graph
        /// will never no-op. We do this to keep this work-around implementation simple.
        /// </summary>
        public DateTimeOffset LastModified => DateTimeOffset.Now;

        public string MSBuildProjectPath => _project.ProjectFullPath;


        public IReadOnlyList<PackageSpec> GetPackageSpecsForRestore(ExternalProjectReferenceContext context)
        {
            return new[] { GetPackageSpec().Result };
        }

        public Boolean IsRestoreRequired(IEnumerable<VersionFolderPathResolver> pathResolvers, ISet<PackageIdentity> packagesChecked, ExternalProjectReferenceContext context)
        {
            //TODO: Make a real evaluation here.
            return true;
        }

        public async Task<IReadOnlyList<ExternalProjectReference>> GetProjectReferenceClosureAsync(
            ExternalProjectReferenceContext context)
        {
            await TaskScheduler.Default;

            var externalProjectReferences = new HashSet<ExternalProjectReference>();

            var packageSpec = await GetPackageSpec();
            if (packageSpec != null)
            {
                var projectReferences = GetProjectReferences(packageSpec);

                var reference = new ExternalProjectReference(
                    packageSpec.RestoreMetadata.ProjectPath,
                    packageSpec,
                    packageSpec.RestoreMetadata.ProjectPath,
                    projectReferences);

                externalProjectReferences.Add(reference);
            }

            return DependencyGraphProjectCacheUtility
                .GetExternalClosure(_project.ProjectFullPath, externalProjectReferences)
                .ToList();
        }

        #endregion

        #region NuGetProject

        public override async Task<IEnumerable<PackageReference>> GetInstalledPackagesAsync(CancellationToken token)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            return GetPackageReferences(await GetPackageSpec());
        }

        public override async Task<Boolean> InstallPackageAsync(PackageIdentity packageIdentity, DownloadResourceResult downloadResourceResult, INuGetProjectContext nuGetProjectContext, CancellationToken token)
        {
            try
            {
                // We don't adjust package reference metadata from UI
                await _project.AddOrUpdateLegacyCSProjPackageAsync(
                    packageIdentity.Id,
                    packageIdentity.Version.ToString(),
                    new string[] { },
                    new string[] { });
            }
            catch (Exception e)
            {
                nuGetProjectContext.Log(MessageLevel.Warning, e.Message, packageIdentity, _project.Name);
                return false;
            }

            return true;
        }

        public override async Task<Boolean> UninstallPackageAsync(PackageIdentity packageIdentity, INuGetProjectContext nuGetProjectContext, CancellationToken token)
        {
            try
            {
                await _project.RemoveLegacyCSProjPackageAsync(packageIdentity.Id);
            }
            catch (Exception e)
            {
                nuGetProjectContext.Log(MessageLevel.Warning, e.Message, packageIdentity, _project.Name);
                return false;
            }

            return true;
        }

        #endregion

        private static string[] GetProjectReferences(PackageSpec packageSpec)
        {
            // There is only one target framework for legacy csproj projects
            var targetFramework = packageSpec.TargetFrameworks.FirstOrDefault();
            if (targetFramework == null)
            {
                return new string[] { };
            }

            return targetFramework.Dependencies
                .Where(d => d.LibraryRange.TypeConstraint == LibraryDependencyTarget.ExternalProject)
                .Select(d => d.LibraryRange.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static PackageReference[] GetPackageReferences(PackageSpec packageSpec)
        {
            var frameworkSorter = new NuGetFrameworkSorter();

            return packageSpec
                .TargetFrameworks
                .SelectMany(f => GetPackageReferences(f.Dependencies, f.FrameworkName))
                .GroupBy(p => p.PackageIdentity)
                .Select(g => g.OrderBy(p => p.TargetFramework, frameworkSorter).First())
                .ToArray();
        }

        private static IEnumerable<PackageReference> GetPackageReferences(IEnumerable<LibraryDependency> libraries, NuGetFramework targetFramework)
        {
            return libraries
                .Where(l => l.LibraryRange.TypeConstraint == LibraryDependencyTarget.Package)
                .Select(l => ToPackageReference(l, targetFramework));
        }

        private static PackageReference ToPackageReference(LibraryDependency library, NuGetFramework targetFramework)
        {
            var identity = new PackageIdentity(
                library.LibraryRange.Name,
                library.LibraryRange.VersionRange.MinVersion);

            return new PackageReference(identity, targetFramework);
        }

        private async Task<PackageSpec> GetPackageSpec()
        {
            var projectReferences = _project.GetLegacyCSProjProjectReferencesAsync(_desiredPackageReferenceMetadata)
                .Result
                .Select(ToProjectRestoreReference);

            var packageReferences = _project.GetLegacyCSProjPackageReferencesAsync(_desiredPackageReferenceMetadata)
                .Result
                .Select(ToPackageLibraryDependency);

            var projectTfi = new TargetFrameworkInformation()
            {
                FrameworkName = await _project.GetTargetNuGetFramework(),
                Dependencies = packageReferences.ToList()
            };

            // In legacy CSProj, we only have one target framework per project
            var tfis = new TargetFrameworkInformation[] { projectTfi };
            var packageSpec = new PackageSpec(tfis)
            {
                Name = _project.Name ?? _project.UniqueName,
                RestoreMetadata = new ProjectRestoreMetadata
                {
                    OutputType = RestoreOutputType.NETCore,
                    ProjectPath = _project.ProjectFullPath,
                    ProjectUniqueName = _project.ProjectFullPath,
                    OriginalTargetFrameworks = tfis
                        .Select(tfi => tfi.FrameworkName.GetShortFolderName())
                        .ToList(),
                    TargetFrameworks = new List<ProjectRestoreMetadataFrameworkInfo>() {
                        new ProjectRestoreMetadataFrameworkInfo(tfis[0].FrameworkName)
                        {
                            ProjectReferences = projectReferences.ToList()
                        }
                    }
                }
            };

            return packageSpec;
        }

        private static ProjectRestoreReference ToProjectRestoreReference(LegacyCSProjProjectReference item)
        {
            var reference = new ProjectRestoreReference()
            {
                ProjectUniqueName = item.UniqueName,
                ProjectPath = item.UniqueName
            };

            MSBuildRestoreUtility.ApplyIncludeFlags(
                reference,
                GetProjectMetadataValue(item, _includeAssets),
                GetProjectMetadataValue(item, _excludeAssets),
                GetProjectMetadataValue(item, _privateAssets));

            return reference;
        }

        //TODO: remove if not needed
        //private static LibraryDependency ToProjectLibraryDependency(ProjectRestoreReference item)
        //{
        //    return new LibraryDependency
        //    {
        //        LibraryRange = new LibraryRange(
        //            name: item.ProjectUniqueName,
        //            versionRange: VersionRange.All,
        //            typeConstraint: LibraryDependencyTarget.ExternalProject)
        //    };
        //}

        //private static PackageReference ToPackageReference(LegacyCSProjPackageReference item)
        //{
        //    return new PackageReference(
        //        identity: new PackageIdentity(
        //            item.Name,
        //            new NuGetVersion(item.Version)),
        //        targetFramework: item.TargetNuGetFramework
        //    );
        //}

        private static LibraryDependency ToPackageLibraryDependency(LegacyCSProjPackageReference item)
        {
            var dependency = new LibraryDependency
            {
                LibraryRange = new LibraryRange(
                    name: item.Name,
                    versionRange: new VersionRange(new NuGetVersion(item.Version)),
                    typeConstraint: LibraryDependencyTarget.Package)
            };

            MSBuildRestoreUtility.ApplyIncludeFlags(
                dependency,
                GetPackageMetadataValue(item, _includeAssets),
                GetPackageMetadataValue(item, _excludeAssets),
                GetPackageMetadataValue(item, _privateAssets));

            return dependency;
        }

        private static string GetProjectMetadataValue(LegacyCSProjProjectReference item, string metadataElement)
        {
            var index = Array.IndexOf(item.MetadataElements, metadataElement);
            if (index >= 0)
            {
                return item.MetadataValues.GetValue(index) as string;
            }

            return string.Empty;
        }

        private static string GetPackageMetadataValue(LegacyCSProjPackageReference item, string metadataElement)
        {
            var index = Array.IndexOf(item.MetadataElements, metadataElement);
            if (index >= 0)
            {
                return item.MetadataValues.GetValue(index) as string;
            }

            return string.Empty;
        }
    }
}
