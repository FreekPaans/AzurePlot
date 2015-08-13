using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using NLog;

namespace AzurePlot.Lib.SQLDatabase
{
    class SysDmDbResourceStatsUsagesClient : ServerUsagesClient
    {
        private readonly string _databaseName;
        readonly SQLDatabaseConnection _connection;
        readonly static Logger _logger = LogManager.GetCurrentClassLogger();

        public SysDmDbResourceStatsUsagesClient(SQLDatabaseConnection connection, string databaseName)
        {
            _connection = connection;
            _databaseName = databaseName;
        }

        public ICollection<UsageObject> GetUsages(DateTime from)
        {
            var result = new List<UsageObject>();
            using (var connection = _connection.GetConnection())
            {
                connection.Open();

                var cmd = connection.CreateCommand();
                cmd.CommandText = "select * from sys.dm_db_resource_stats where end_time > @from";
                cmd.Parameters.Add(new System.Data.SqlClient.SqlParameter("from", from));
                cmd.CommandTimeout = (int)TimeSpan.FromMinutes(1).TotalSeconds; ;

                try
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            result.AddRange(GetResultFromReader(reader));
                        }
                    }
                }
                catch (SqlException e)
                {
                    //swallow the exception, it might be we still got results..
                    //this is a fixed for a problem where sql azure throws an error "Unable to retrieve Azure SQL Database telemetry data" (error code 25745),  but still returns results
                    _logger.Log(LogLevel.Error, "Reading resource stats failed", e);
                }
            }
            return result;
        }

        readonly static string[] NotCounterColumns = new string[] { "end_time" };

        private ICollection<UsageObject> GetResultFromReader(SqlDataReader reader)
        {
            var timestamp = DateTime.SpecifyKind((DateTime)reader["end_time"], DateTimeKind.Utc);
            var result = new List<UsageObject>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                if (NotCounterColumns.Contains(name))
                {
                    continue;
                }

                var valueResult = GetDouble(reader[i]);

                if (valueResult == null)
                {
                    continue;
                }

                result.Add(new UsageObject { Timestamp = timestamp.ToString("o"), Value = (double)valueResult, GraphiteCounterName = GetCounterName(_databaseName, name) });
            }

            return result;
        }

        private static double? GetDouble(object value)
        {
            double? valueResult = null;

            if (value is decimal)
            {
                valueResult = (double)(decimal)value;
            }
            if (value is int)
            {
                valueResult = (double)(int)value;
            }
            if (value is long)
            {
                valueResult = (double)(long)value;
            }
            if (value is double)
            {
                valueResult = (double)value;
            }
            return valueResult;
        }

        private string GetCounterName(string database, string name)
        {
            return new GraphiteCounterName("Azure.SQLDatabase", _connection.Servername, database, name).ToString();
        }
    }
}