using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AzurePlot.Lib.SQLDatabase {
	public class SQLDatabaseUsageClient {
		readonly SQLDatabaseConnection _connection;

		SQLDatabaseUsageClient(SQLDatabaseConnection connection) {
			_connection = connection;
		}
		public static SQLDatabaseUsageClient CreateServerUsagesClient(string servername,string username,string password) {
			return new SQLDatabaseUsageClient(new SQLDatabaseConnection(servername,username,password,"master"));
			
		}

        public static SQLDatabaseStatsClient CreateDatabaseUsagesClient(string serverName,string database,string username,string password) {
            return new SQLDatabaseStatsClient(new SQLDatabaseConnection(serverName,username,password,database));
        }


        public TestConnectionResult TestConnection() {
			var result = _connection.TestOpenConnection();
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
				_version = _connection.GetVersion();
			}
			return _version;
		}

		public ICollection<UsageObject> GetUsages(DateTime fromTimeUTC) {
			return GetUsagesClient().GetUsages(fromTimeUTC);
		}

		private ServerUsagesClient GetUsagesClient() {
			var version = GetVersion();
			switch(version.Version) {
				case SQLDatabaseVersionEnum.V11:
                case SQLDatabaseVersionEnum.V12:
                case SQLDatabaseVersionEnum.Unknown:
					return new SysResourceStatsUsagesClient(_connection);
                
			}

			throw new Exception(string.Format("Version not supported {0}",version));
		}

		public static string NormalizeServername(string servername) {
			return SQLDatabaseConnection.NormalizeServername(servername);
		}

        public static ICollection<string> ListUserDatabases(string servername,string username,string password) {
            var connection = new SQLDatabaseConnection(servername,username,password,"master");
            var res = new List<string>();
            using(var conn = connection.GetConnection()) {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = " select * from sys.databases";
                using(var reader = cmd.ExecuteReader()) {
                    while(reader.Read()) {
                        var name = (string)reader["name"];
                        if(name=="master") {
                            continue;
                        }
                        res.Add(name);
                    }
                    return res;
                }
            }
        }

        public string GetVersionString() {
			return GetVersion().DetailedVersion;
		}

        public string ServerName {
            get {
                return _connection.Servername;
            }
        }

        internal List<string> ListDatabases() {
            return GetUsagesClient().ListDatabases();
        }

        
    }
}
