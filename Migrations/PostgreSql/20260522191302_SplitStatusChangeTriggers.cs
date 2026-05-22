using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniMap360.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class SplitStatusChangeTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                -- 1. Tạo function mới cho HoSoUngTuyen
                CREATE OR REPLACE FUNCTION fn_LogJobApplicationStatusChange() RETURNS TRIGGER AS $$
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
                            'Thay đổi trạng thái HoSoUngTuyen ID ' || NEW.""ApplicationID""::text || ' sang ' || NEW.""Status"",
                            NOW()
                        );
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
            ");

            migrationBuilder.Sql(@"
                -- 2. Tạo function mới cho LichXemPhong
                CREATE OR REPLACE FUNCTION fn_LogAppointmentStatusChange() RETURNS TRIGGER AS $$
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
                            'Thay đổi trạng thái LichXemPhong ID ' || NEW.""AppointmentID""::text || ' sang ' || NEW.""Status"",
                            NOW()
                        );
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
            ");

            migrationBuilder.Sql(@"
                -- 3. Cập nhật trigger trên HoSoUngTuyen
                DROP TRIGGER IF EXISTS tr_hosoungtuyen_logtrangthai ON ""HoSoUngTuyen"";
                CREATE TRIGGER tr_hosoungtuyen_logtrangthai
                AFTER UPDATE ON ""HoSoUngTuyen""
                FOR EACH ROW EXECUTE FUNCTION fn_LogJobApplicationStatusChange();
            ");

            migrationBuilder.Sql(@"
                -- 4. Cập nhật trigger trên LichXemPhong
                DROP TRIGGER IF EXISTS tr_lichxemphong_logtrangthai ON ""LichXemPhong"";
                CREATE TRIGGER tr_lichxemphong_logtrangthai
                AFTER UPDATE ON ""LichXemPhong""
                FOR EACH ROW EXECUTE FUNCTION fn_LogAppointmentStatusChange();
            ");

            migrationBuilder.Sql(@"
                -- 5. Xóa function cũ
                DROP FUNCTION IF EXISTS fn_logstatuschange();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                -- 1. Tạo lại function cũ
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

            migrationBuilder.Sql(@"
                -- 2. Cập nhật lại trigger trên HoSoUngTuyen gọi function cũ
                DROP TRIGGER IF EXISTS tr_hosoungtuyen_logtrangthai ON ""HoSoUngTuyen"";
                CREATE TRIGGER tr_hosoungtuyen_logtrangthai
                AFTER UPDATE ON ""HoSoUngTuyen""
                FOR EACH ROW EXECUTE FUNCTION fn_LogStatusChange();
            ");

            migrationBuilder.Sql(@"
                -- 3. Cập nhật lại trigger trên LichXemPhong gọi function cũ
                DROP TRIGGER IF EXISTS tr_lichxemphong_logtrangthai ON ""LichXemPhong"";
                CREATE TRIGGER tr_lichxemphong_logtrangthai
                AFTER UPDATE ON ""LichXemPhong""
                FOR EACH ROW EXECUTE FUNCTION fn_LogStatusChange();
            ");

            migrationBuilder.Sql(@"
                -- 4. Xóa 2 function mới
                DROP FUNCTION IF EXISTS fn_LogJobApplicationStatusChange();
                DROP FUNCTION IF EXISTS fn_LogAppointmentStatusChange();
            ");
        }
    }
}
