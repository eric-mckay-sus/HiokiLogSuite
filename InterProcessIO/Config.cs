// <copyright file="Config.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace InterProcessIO;

using StringBuilder = Microsoft.Data.SqlClient.SqlConnectionStringBuilder;

/// <summary>
/// A container for the data that is constant across the solution (but could change).
/// </summary>
public static class Config
{
    /// <summary>
    /// Gets or sets the default input location for the CMMS-line mapping uploader.
    /// </summary>
    public static string InputLocation { get; set; } = @"C:/LOCAL NETWORK FILES/Hioki ICT results/20251212/";

    /// <summary>
    /// Gets the connection string for the database whose credentials are stored in environment variables.
    /// </summary>
    /// <returns>A SQL Server connection string for access to the database.</returns>
    /// <throws>InvalidOperationException when there are missing environment variable(s).</throws>
    public static string GetConnectionString()
    {
        static string GetRequired(string key)
        {
            string? value = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Required environment variable '{key}' is missing for database connection.");
            }

            return value;
        }

        StringBuilder builder = new ()
        {
            DataSource = GetRequired("DB_SERVER"),
            UserID = GetRequired("DB_USER"),
            Password = GetRequired("DB_PASS"),
            InitialCatalog = GetRequired("HIOKI_DB_NAME"),
            TrustServerCertificate = true,
        };
        return builder.ConnectionString;
    }
}
