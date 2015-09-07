using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;

namespace AzurePlot.Lib.SQLDatabase {
    public class SQLDatabaseStatsClient {
        readonly static Logger _logger = LogManager.GetCurrentClassLogger();
        
        readonly SQLDatabaseConnection _connection;

        internal SQLDatabaseStatsClient(SQLDatabaseConnection connection) {
            _connection = connection;
        }

        public ICollection<UsageObject> GetUsages() {
            if(!CanFetchResourceStatsCached()) {
                return new UsageObject[0];
            }


            return RunQuery();
        }

        private bool CanFetchResourceStatsCached() {
            bool val;
            var key = GetCacheKey();

            if(_canReadStats.TryGetValue(key, out val)) {
                _logger.Log(LogLevel.Debug,"returning from cache: {0}={1}", key,val);
                return val;
            }

            var result = CanFetchResourceStats();
            _canReadStats[key] = result;
            return result;
        }

        private string GetCacheKey() {
            return string.Format("{0}:{1}:{2}", _connection.Servername, _connection.Database,_connection.Username);
        }

        readonly static ConcurrentDictionary<string,bool> _canReadStats = new ConcurrentDictionary<string, bool>();

        bool CanFetchResourceStats() {
            var connectionResult = _connection.TestOpenConnection();
            if(connectionResult != TestConnectionResult.Success) {
                _logger.Log(LogLevel.Error,"Couldn't connect to database for sys.dm_db_resource_stats",connectionResult.Exception);
                return false;
            }

            if(!HasViewDatabasePermission()) {
                _logger.Log(LogLevel.Error,"User {0} doesn't have VIEW DATABASE STATE permission for {1}",_connection.Username,_connection.Database);
                return false;
            }

            return true;
        }


        readonly static string[] NotCounterColumns = new string[] { "end_time"};

        private ICollection<UsageObject> RunQuery() {
            var result = new List<UsageObject>();

            using(var connection = _connection.GetConnection()) {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "select * from sys.dm_db_resource_stats order by end_time asc";
                connection.Open();
                using(var reader = cmd.ExecuteReader()) {
                    while(reader.Read()) {
                        var time = DateTime.SpecifyKind((DateTime)reader["end_time"], DateTimeKind.Utc);
                        
                        for(var i=0; i<reader.FieldCount;i++) { 
                            var name = reader.GetName(i);
                            if(NotCounterColumns.Contains(name)) {
                                continue;
                            }

                            var value = (decimal)reader[i];

                            result.Add(new UsageObject {
                                Timestamp = time.ToString("o"),
                                Value = (double)value,
                                GraphiteCounterName = new GraphiteCounterName("Azure.SQLRealTime",_connection.Servername,_connection.Database,name).ToString()
                            });
                        }
                    }
                }
                return result;
            }
        }

        const string HasViewDatabaseStateSql = @"
select count(*) from sys.database_principals prin

left join sys.database_permissions perm on prin.principal_id = perm.grantee_principal_id

where prin.name=@User and permission_name = 'VIEW DATABASE STATE'";

        private bool HasViewDatabasePermission() {
            using(var connection = _connection.GetConnection()) {
                var cmd = connection.CreateCommand();
                cmd.CommandText = HasViewDatabaseStateSql;
                cmd.Parameters.Add(new SqlParameter("User", _connection.Username));
                connection.Open();
                var result = cmd.ExecuteScalar();
                return (int)result!=0;
            }
        }
    }
}