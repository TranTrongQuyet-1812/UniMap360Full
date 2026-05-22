using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace UniMap360.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class FixLogStatusChangeTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION fn_LogStatusChange() RETURNS TRIGGER AS $$
                DECLARE
                    v_AccountId INTEGER;
                BEGIN
                    IF (TG_OP = 'UPDATE' AND OLD.""Status"" IS DISTINCT FROM NEW.""Status"") THEN
                        SELECT ""AccountID"" INTO v_AccountId 
                        FROM ""HoSoSinhVien"" 
                        WHERE ""StudentID"" = NEW.""StudentID"";

                        INSERT INTO ""NhatKyHeThong"" (""AccountID"", ""LogAction"", ""ActionTime"")
                        VALUES (
                            v_AccountId,
                            'Thay đổi trạng thái ' || TG_TABLE_NAME || ' ID ' || 
                            CASE 
                                WHEN TG_TABLE_NAME = 'HoSoUngTuyen' THEN NEW.""ApplicationID""::text
                                WHEN TG_TABLE_NAME = 'LichXemPhong' THEN NEW.""AppointmentID""::text
                                ELSE '?'
                            END || ' sang ' || NEW.""Status"",
                            NOW()
                        );
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION fn_LogStatusChange() RETURNS TRIGGER AS $$
                BEGIN
                    IF (TG_OP = 'UPDATE' AND OLD.""Status"" IS DISTINCT FROM NEW.""Status"") THEN
                        INSERT INTO ""NhatKyHeThong"" (""AccountID"", ""LogAction"", ""ActionTime"")
                        VALUES (
                            CASE 
                                WHEN TG_TABLE_NAME = 'HoSoUngTuyen' THEN NEW.""StudentID""
                                WHEN TG_TABLE_NAME = 'LichXemPhong' THEN NEW.""StudentID""
                                ELSE NULL
                            END,
                            'Thay đổi trạng thái ' || TG_TABLE_NAME || ' ID ' || 
                            CASE 
                                WHEN TG_TABLE_NAME = 'HoSoUngTuyen' THEN NEW.""ApplicationID""::text
                                WHEN TG_TABLE_NAME = 'LichXemPhong' THEN NEW.""AppointmentID""::text
                                ELSE '?'
                            END || ' sang ' || NEW.""Status"",
                            NOW()
                        );
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
            ");
        }
    }
}
