﻿namespace ServiceControlInstaller.Engine.Validation
{
    using System;
    using System.Data.Common;
    using Microsoft.Data.SqlClient;
    using System.Linq;
    using Accounts;
    using Instances;

    class ConnectionStringValidator
    {
        ConnectionStringValidator(string connectionString, string serviceAccount)
        {
            this.connectionString = connectionString;
            this.serviceAccount = serviceAccount;
        }

        public static void Validate(IServiceControlAuditInstance instance)
        {
            var validator = new ConnectionStringValidator(instance.ConnectionString, instance.ServiceAccount);
            if (instance.TransportPackage.Name.Equals(TransportNames.SQLServer, StringComparison.OrdinalIgnoreCase))
            {
                validator.CheckMsSqlConnectionString();
            }
        }

        public static void Validate(IServiceControlInstance instance)
        {
            var validator = new ConnectionStringValidator(instance.ConnectionString, instance.ServiceAccount);
            if (instance.TransportPackage.Name.Equals(TransportNames.SQLServer, StringComparison.OrdinalIgnoreCase))
            {
                validator.CheckMsSqlConnectionString();
            }
        }

        public static void Validate(IMonitoringInstance instance)
        {
            var validator = new ConnectionStringValidator(instance.ConnectionString, instance.ServiceAccount);
            if (instance.TransportPackage.Name.Equals(TransportNames.SQLServer, StringComparison.OrdinalIgnoreCase))
            {
                validator.CheckMsSqlConnectionString();
            }
        }

        void CheckMsSqlConnectionString()
        {
            string[] customKeys = { "Queue Schema", "Subscriptions Table" };

            try
            {
                //Check  validity of connection string. This will throw if invalid
                var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };

                //The NSB SQL Transport can have custom key/value pairs in the connection string
                // that won't make sense to SQL. Remove these from the string we want to validate.
                foreach (var customKey in customKeys)
                {
                    if (builder.ContainsKey(customKey))
                    {
                        builder.Remove(customKey);
                    }
                }

                //Check that localsystem is not used when integrated security is enabled
                if (builder.ContainsKey("Integrated Security"))
                {
                    var integratedSecurity = (string)builder["Integrated Security"];
                    var enabledValues = new[]
                    {
                        "true",
                        "yes",
                        "sspi"
                    };
                    if (enabledValues.Any(p => p.Equals(integratedSecurity, StringComparison.OrdinalIgnoreCase)))
                    {
                        var account = UserAccount.ParseAccountName(serviceAccount);
                        if (account.IsLocalSystem())
                        {
                            throw new EngineValidationException("Invalid service account for this connection string. The connection string has integrated security enabled but localsystem service has been selected.");
                        }
                    }
                }

                //Attempt to connect to DB
                using (var s = new SqlConnection(builder.ConnectionString))
                {
                    s.Open();
                }
            }
            catch (ArgumentException argumentException)
            {
                throw new EngineValidationException($"Connection String is invalid - {argumentException.Message}");
            }
            catch (SqlException sqlEx)
            {
                throw new EngineValidationException($"SQL connection failed - {sqlEx.Message}");
            }
        }

        string connectionString;
        string serviceAccount;
    }
}