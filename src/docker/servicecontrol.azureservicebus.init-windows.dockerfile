FROM mcr.microsoft.com/windows/servercore:ltsc2016

WORKDIR /servicecontrol

ADD /ServiceControl.Transports.ASBS/bin/Release/net462 .
ADD /ServiceControl/bin/Release/net462 .

ENV "IsDocker"="true"

ENV "ServiceControl/TransportType"="ServiceControl.Transports.ASBS.ASBSTransportCustomization, ServiceControl.Transports.ASBS"
ENV "ServiceControl/Hostname"="*"

ENV "ServiceControl/DBPath"="C:\\Data\\DB\\"
ENV "ServiceControl/LogPath"="C:\\Data\\Logs\\"

# Defaults
ENV "ServiceControl/ForwardErrorMessages"="False"
ENV "ServiceControl/ErrorRetentionPeriod"="15"

ENTRYPOINT ["ServiceControl.exe", "--portable", "--setup"]