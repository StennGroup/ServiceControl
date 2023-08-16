FROM mcr.microsoft.com/dotnet/framework/runtime:4.7.2-windowsservercore-ltsc2019

WORKDIR /servicecontrol.audit

ADD /ServiceControl.Transports.ASBS/bin/Release/net472 .
ADD /ServiceControl.Audit/bin/Release/net472 .

ENV "SERVICECONTROL_RUNNING_IN_DOCKER"="true"

ENV "ServiceControl.Audit/TransportType"="ServiceControl.Transports.ASBS.ASBSTransportCustomization, ServiceControl.Transports.ASBS"
ENV "ServiceControl.Audit/Hostname"="*"

EXPOSE 44444
EXPOSE 44445

VOLUME [ "C:/Data" ]

ENTRYPOINT ["ServiceControl.Audit.exe", "--portable"]
