FROM mcr.microsoft.com/dotnet/framework/runtime:4.7.2-windowsservercore-ltsc2022

WORKDIR /servicecontrol.audit

ADD /ServiceControl.Transports.ASBS/bin/Release/net472 .
ADD /ServiceControl.Audit/bin/Release/net472 .

ENV "SERVICECONTROL_RUNNING_IN_DOCKER"="true"

ENV "ServiceControl.Audit/TransportType"="ServiceControl.Transports.ASBS.ASBSTransportCustomization, ServiceControl.Transports.ASBS"
ENV "ServiceControl.Audit/Hostname"="*"

VOLUME [ "C:/Data" ]

ENTRYPOINT ["ServiceControl.Audit.exe", "--portable", "--setup"]
