namespace ServiceControlInstaller.Engine.Validation
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;

    class PathsValidator
    {
        public PathsValidator(IServicePaths instance)
        {
            var pathList = new List<PathInfo>
            {
                new PathInfo
                {
                    Name = "log path",
                    Path = Environment.ExpandEnvironmentVariables(instance.LogPath ?? string.Empty)
                },
                new PathInfo
                {
                    Name = "install path",
                    Path = Environment.ExpandEnvironmentVariables(instance.InstallPath ?? string.Empty),
                    CheckIfEmpty = true
                }
            };
            paths = pathList.Where(p => !string.IsNullOrWhiteSpace(p.Path)).ToList();
        }

        public PathsValidator(IServiceControlPaths instance)
        {
            var pathList = new List<PathInfo>
            {
                new PathInfo
                {
                    Name = "log path",
                    Path = Environment.ExpandEnvironmentVariables(instance.LogPath ?? string.Empty)
                },
                new PathInfo
                {
                    Name = "DB path",
                    Path = Environment.ExpandEnvironmentVariables(instance.DBPath ?? string.Empty),
                    CheckIfEmpty = true
                },
                new PathInfo
                {
                    Name = "install path",
                    Path = Environment.ExpandEnvironmentVariables(instance.InstallPath ?? string.Empty),
                    CheckIfEmpty = true
                }
            };
            paths = pathList.Where(p => !string.IsNullOrWhiteSpace(p.Path)).ToList();
        }

        public Task RunValidation(bool includeNewInstanceChecks)
        {
            return RunValidation(includeNewInstanceChecks, info => Task.FromResult(false));
        }

        public async Task<bool> RunValidation(bool includeNewInstanceChecks, Func<PathInfo, Task<bool>> promptToProceed)
        {
            try
            {
                CheckPathsAreValid();
                CheckNoNestedPaths();
                CheckPathsAreUnique();

                var cancelRequested = false;
                //Do Checks that only make sense on add instance
                if (includeNewInstanceChecks)
                {
                    cancelRequested = await CheckPathsAreEmpty(promptToProceed).ConfigureAwait(false);
                }

                return cancelRequested;
            }
            catch (EngineValidationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new EngineValidationException("An unhandled exception occured while trying to validate the paths.", ex);
            }
        }

        async Task<bool> CheckPathsAreEmpty(Func<PathInfo, Task<bool>> promptToProceed)
        {
            foreach (var pathInfo in paths)
            {
                if (!pathInfo.CheckIfEmpty)
                {
                    continue;
                }

                var directory = new DirectoryInfo(pathInfo.Path);
                if (directory.Exists)
                {
                    var flagFile = Path.Combine(directory.FullName, ".notconfigured");
                    if (File.Exists(flagFile))
                    {
                        continue; // flagfile will be present if we've unpacked and had a config failure.  In this case it's OK for the directory to have content
                    }

                    if (directory.EnumerateFileSystemInfos().Any())
                    {
                        var shouldProceed = await promptToProceed(pathInfo).ConfigureAwait(false);
                        if (!shouldProceed)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }


        internal void CheckNoNestedPaths()
        {
            foreach (var path in paths)
            {
                foreach (var possibleChild in paths)
                {
                    if (path.Name == possibleChild.Name)
                    {
                        continue;
                    }

                    if (Path.GetDirectoryName(possibleChild.Path).IndexOf(path.Path, StringComparison.OrdinalIgnoreCase) > -1)
                    {
                        throw new EngineValidationException($"Nested paths are not supported. The {possibleChild.Name} is nested under {path.Name}");
                    }
                }
            }
        }

        internal void CheckPathsAreUnique()
        {
            if (paths.Select(p => p.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Count)
            {
                throw new EngineValidationException("The installation path, log path and database path must be unique");
            }
        }

        internal void CheckPathsAreValid()
        {
            var driveletters = DriveInfo.GetDrives().Where(p => p.DriveType != DriveType.Network && p.DriveType != DriveType.CDRom)
                .Select(p => p.Name[0].ToString())
                .ToArray();

            foreach (var path in paths)
            {
                if (!Uri.TryCreate(path.Path, UriKind.Absolute, out var uri))
                {
                    throw new EngineValidationException($"The {path.Name} is set to an invalid path");
                }

                if (uri.IsUnc)
                {
                    throw new EngineValidationException($"The {path.Name} is invalid,  UNC paths are not supported.");
                }

                if (uri.Scheme != Uri.UriSchemeFile)
                {
                    throw new EngineValidationException($"The {path.Name} is set to an invalid path");
                }

                if (!Path.IsPathRooted(uri.LocalPath))
                {
                    throw new EngineValidationException($"A full path is required for {path.Name}");
                }

                var pathDriveLetter = Path.GetPathRoot(uri.LocalPath)[0].ToString();
                if (!driveletters.Contains(pathDriveLetter, StringComparer.OrdinalIgnoreCase))
                {
                    throw new EngineValidationException($"The {path.Name} does not go to a supported drive");
                }
            }
        }

        List<PathInfo> paths;
    }
}