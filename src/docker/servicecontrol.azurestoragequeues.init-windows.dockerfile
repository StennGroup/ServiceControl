FROM mcr.microsoft.com/windows/servercore:ltsc2016

WORKDIR /servicecontrol

ADD /ServiceControl.Transports.ASQ/bin/Release/net462 .
ADD /ServiceControl/bin/Release/net462 .

ENV "ServiceControl/TransportType"="ServiceControl.Transports.ASQ.ASQTransportCustomization, ServiceControl.Transports.ASQ"
ENV "ServiceControl/Hostname"="*"

ENV "ServiceControl/DBPath"="C:\\Data\\"
ENV "ServiceControl/LogPath"="C:\\Logs\\"

# Defaults
ENV "ServiceControl/ForwardErrorMessages"="False"
ENV "ServiceControl/ErrorRetention"="15"

ENTRYPOINT ["ServiceControl.exe", "--portable", "--setup"]