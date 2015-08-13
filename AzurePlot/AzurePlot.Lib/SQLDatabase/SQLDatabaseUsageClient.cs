using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AzurePlot.Lib.SQLDatabase {
	public class SQLDatabaseUsageClient {
		readonly SQLDatabaseConnection _masterDatabaseConnection;
	    private readonly Func<string, SQLDatabaseConnection> _createDatabaseConnectionFunc;

	    SQLDatabaseUsageClient(SQLDatabaseConnection masterDatabaseConnection, Func<string, SQLDatabaseConnection> createDatabaseConnectionFunc)
	    {
	        _masterDatabaseConnection = masterDatabaseConnection;
	        _createDatabaseConnectionFunc = createDatabaseConnectionFunc;
	    }

	    public static SQLDatabaseUsageClient CreateServerUsagesClient(string servername,string username,string password) {
			return new SQLDatabaseUsageClient(
                new SQLDatabaseConnection(servername,username,password,"master"),
                name => new SQLDatabaseConnection(servername, username, password, name));
			
		}

		public TestConnectionResult TestConnection() {
			var result = _masterDatabaseConnection.TestOpenConnection();
			if(result.Failed) {
				return result;
			}
			return TestVersion();
		}

		private TestConnectionResult TestVersion() {
			var version = GetVersion();
			if(version.Version == SQLDatabaseVersionEnum.Unknown) {
				return new TestConnectionResult(string.Format("Not supported SQL Server version ({0}), currently only V11 and V12 databases are supported",version.DetailedVersion), null);
			}

			return TestConnectionResult.Success;
		}

		SQLDatabaseVersion _version = null;

		private SQLDatabaseVersion GetVersion() {
			if(_version == null) {
				_version = _masterDatabaseConnection.GetVersion();
			}
			return _version;
		}

		public ICollection<UsageObject> GetUsages(DateTime fromTimeUTC)
		{
		    var usages = GetSysResourceStatsUsagesClient().GetUsages(fromTimeUTC);

		    var databases = ListDatabases();

		    foreach (var databaseName in databases)
		    {
		        var sqlDatabaseConnection = _createDatabaseConnectionFunc(databaseName);
                var usagesClient = GetSysDmDbResourceStats(sqlDatabaseConnection, databaseName);

                usages = usages.Concat(usagesClient.GetUsages(fromTimeUTC)).ToList();
		    }

            return usages;
		}

		private ServerUsagesClient GetSysResourceStatsUsagesClient() {
			var version = GetVersion();
			switch(version.Version) {
				case SQLDatabaseVersionEnum.V11:
                case SQLDatabaseVersionEnum.V12:
                case SQLDatabaseVersionEnum.Unknown:
					return new SysResourceStatsUsagesClient(_masterDatabaseConnection);
			}

			throw new Exception($"Version not supported {version}");
		}

	    private ServerUsagesClient GetSysDmDbResourceStats(SQLDatabaseConnection sqlDatabaseConnection, string databaseName)
	    {
            var version = GetVersion();
            switch (version.Version)
            {
                case SQLDatabaseVersionEnum.V11:
                case SQLDatabaseVersionEnum.V12:
                case SQLDatabaseVersionEnum.Unknown:
                    return new SysDmDbResourceStatsUsagesClient(sqlDatabaseConnection, databaseName);
            }

            throw new Exception($"Version not supported {version}");
        }

        public static string NormalizeServername(string servername) {
			return SQLDatabaseConnection.NormalizeServername(servername);
		}

		public string GetVersionString() {
			return GetVersion().DetailedVersion;
		}

        public string ServerName {
            get {
                return _masterDatabaseConnection.Servername;
            }
        }

        internal List<string> ListDatabases() {
            var result = new List<string>();

            using (var connection = _masterDatabaseConnection.GetConnection())
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "select distinct(database_name) from sys.resource_stats where start_time>=@yesterday";

                cmd.Parameters.Add(new System.Data.SqlClient.SqlParameter("yesterday", DateTime.UtcNow.AddDays(-1).Date));

                cmd.CommandTimeout = (int)TimeSpan.FromMinutes(1).TotalSeconds;

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result.Add((string)reader["database_name"]);
                    }
                }
            }
            return result;
        }
    }
}
