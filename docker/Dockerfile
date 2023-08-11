#FROM mcr.microsoft.com/windows/servercore
#FROM mcr.microsoft.com/windows/server
FROM mcr.microsoft.com/dotnet/framework/runtime:4.7.2-windowsservercore-ltsc2019
FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build


# RUN nuget restore D:\a\1\s\src\ServiceControl.sln -Verbosity Detailed -NonInteractive -ConfigFile D:\a\1\s\nuget.config


COPY src src
COPY nuget.config .



WORKDIR /src
RUN dotnet restore --configfile ..\nuget.config
#ENTRYPOINT [ "ping", "-t", "localhost" ] 

 
#/src/ServiceControl.sln -Verbosity Detailed -NonInteractive -ConfigFile nuget.config


#         # solution: src\ServiceControl.sln
#         # vsVersion: 16.0
#         # platform: any cpu
#         # configuration: Release
#         # maximumCpuCount: true
#         # msbuildArchitecture: x64
# # RUN dotnet restore

RUN dotnet build 
#ServiceControl.sln /nologo /nr:false SolutionDir=src /p:platform="any cpu" /p:configuration="Release" /p:VisualStudioVersion="16.0" /m
