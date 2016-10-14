﻿#if VS15
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.ProjectManagement;
using NuGet.ProjectModel;
using NuGet.Protocol.Core.Types;
using VSLangProj150;

namespace NuGet.PackageManagement.VisualStudio
{
/// <summary>
/// An implementation of <see cref="NuGetProject"/> that interfaces with VS project APIs to coordinate
/// packages in a legacy CSProj with package references.
/// </summary>
    public class LegacyCSProjPackageReferenceProject : NuGetProject
    {
        private readonly string _projectName;
        private readonly string _projectUniqueName;
        private readonly string _projectFullPath;
        private readonly PackageReferences _packageReferences;

       // private readonly Func<PackageSpec> _packageSpecFactory;

        public LegacyCSProjPackageReferenceProject(
            string projectName, 
            string projectUniqueName, 
            string projectFullPath, 
            PackageReferences packageReferences/*, 
            Func<PackageSpec> packageSpecFactory*/)
        {
            if (projectFullPath == null)
            {
                throw new ArgumentNullException(nameof(projectFullPath));
            }

          /*  if (packageSpecFactory == null)
            {
                throw new ArgumentNullException(nameof(packageSpecFactory));
            }*/

            _projectName = projectName;
            _projectUniqueName = projectUniqueName;
            _projectFullPath = projectFullPath;
            _packageReferences = packageReferences;

            //_packageSpecFactory = packageSpecFactory;

            InternalMetadata.Add(NuGetProjectMetadataKeys.Name, _projectName);
            InternalMetadata.Add(NuGetProjectMetadataKeys.UniqueName, _projectUniqueName);
            InternalMetadata.Add(NuGetProjectMetadataKeys.FullPath, _projectFullPath);
        }

        #region NuGetProject

        public override Task<IEnumerable<PackageReference>> GetInstalledPackagesAsync(CancellationToken token)
        {
            //TODO: Figure out the right API to call the list of packages installed.
            //            var configuredProject = await _packageReferences.TryGetReference()
            var list = Enumerable.Empty<PackageReference>();
            return System.Threading.Tasks.Task.FromResult(list);
        }

        public override Task<Boolean> InstallPackageAsync(PackageIdentity packageIdentity, DownloadResourceResult downloadResourceResult, INuGetProjectContext nuGetProjectContext, CancellationToken token)
        {
            try
            {
                _packageReferences.AddOrUpdate(packageIdentity.Id, packageIdentity.Version.ToString(), new string[] { }, new string[] { });
            }
            catch (Exception e)
            {
                System.Console.Write(e);
            }
            //var result = await
            //    configuredProject.Services.PackageReferences.AddAsync
            //    (packageIdentity.Id, packageIdentity.Version.ToString());
            //if (!result.Added)
            //{
            //    var existingReference = result.Reference;
            //    await existingReference.Metadata.SetPropertyValueAsync("Version", packageIdentity.Version.ToString());
            //}

            //TODO: Set additional metadata here.
            return System.Threading.Tasks.Task.FromResult(true);
        }

        public override Task<Boolean> UninstallPackageAsync(PackageIdentity packageIdentity, INuGetProjectContext nuGetProjectContext, CancellationToken token)
        {
            //            var configuredProject = await _unconfiguredProject.GetSuggestedConfiguredProjectAsync();
            //            await configuredProject.Services.PackageReferences.RemoveAsync(packageIdentity.Id);
            return System.Threading.Tasks.Task.FromResult(true);
        }

#endregion
    }
}
#endif
