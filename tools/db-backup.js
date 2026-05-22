const { Client } = require('pg');
const fs = require('fs');
const path = require('path');

async function backup() {
    try {
        console.log("Loading credentials from secrets.ini...");
        const iniPath = path.join(__dirname, '..', 'secrets.ini');
        if (!fs.existsSync(iniPath)) {
            throw new Error(`secrets.ini not found at ${iniPath}`);
        }

        const iniContent = fs.readFileSync(iniPath, 'utf8');
        const connLine = iniContent.split(/\r?\n/).find(line => line.startsWith('DefaultConnection='));
        if (!connLine) {
            throw new Error("DefaultConnection connection string not found in secrets.ini");
        }

        const connStr = connLine.slice('DefaultConnection='.length).trim();
        const params = {};
        connStr.split(';').forEach(part => {
            const eqIdx = part.indexOf('=');
            if (eqIdx !== -1) {
                const key = part.slice(0, eqIdx).trim().toLowerCase();
                const value = part.slice(eqIdx + 1).trim();
                params[key] = value;
            }
        });

        const config = {
            host: params.host,
            port: parseInt(params.port || '5432'),
            database: params.database,
            user: params.username || params.user,
            password: params.password,
            ssl: { rejectUnauthorized: false }
        };

        console.log(`Connecting to PostgreSQL database at ${config.host}...`);
        const client = new Client(config);
        await client.connect();
        console.log("Connected successfully!");

        // 1. Get all base tables in the public schema
        const resTables = await client.query(`
            SELECT table_name 
            FROM information_schema.tables 
            WHERE table_schema = 'public' AND table_type = 'BASE TABLE' AND table_name NOT LIKE '__EFMigrationsHistory'
            ORDER BY table_name;
        `);
        const tables = resTables.rows.map(r => r.table_name);
        console.log(`Found ${tables.length} tables to backup.`);

        let sqlOutput = `-- UniMap360 Local Database Backup (Data Only)\n`;
        sqlOutput += `-- Generated on: ${new Date().toLocaleString()}\n`;
        sqlOutput += `-- Host: ${config.host}\n\n`;
        sqlOutput += `SET statement_timeout = 0;\n`;
        sqlOutput += `SET lock_timeout = 0;\n`;
        sqlOutput += `SET client_encoding = 'UTF8';\n\n`;

        // 2. Iterate through each table and generate INSERT statements
        for (const table of tables) {
            console.log(`Exporting table: "${table}"...`);
            
            // Get columns in defined order
            const resCols = await client.query(`
                SELECT column_name 
                FROM information_schema.columns 
                WHERE table_name = $1 AND table_schema = 'public'
                ORDER BY ordinal_position;
            `, [table]);
            const columns = resCols.rows.map(r => r.column_name);

            sqlOutput += `\n-- -----------------------------------------------------\n`;
            sqlOutput += `-- Data for Table: "${table}"\n`;
            sqlOutput += `-- -----------------------------------------------------\n`;
            
            // Disable triggers/foreign keys temporary or clean table
            sqlOutput += `TRUNCATE TABLE "${table}" RESTART IDENTITY CASCADE;\n`;

            const resRows = await client.query(`SELECT * FROM "${table}"`);
            
            if (resRows.rows.length === 0) {
                sqlOutput += `-- No records found in "${table}"\n`;
                continue;
            }

            for (const row of resRows.rows) {
                const colNames = [];
                const valStrings = [];

                for (const col of columns) {
                    colNames.push(`"${col}"`);
                    const val = row[col];
                    if (val === null || val === undefined) {
                        valStrings.push('NULL');
                    } else if (val instanceof Date) {
                        valStrings.push(`'${val.toISOString()}'`);
                    } else if (typeof val === 'string') {
                        valStrings.push(`'${val.replace(/'/g, "''")}'`);
                    } else if (typeof val === 'boolean') {
                        valStrings.push(val ? 'true' : 'false');
                    } else if (typeof val === 'object') {
                        valStrings.push(`'${JSON.stringify(val).replace(/'/g, "''")}'`);
                    } else {
                        valStrings.push(val);
                    }
                }

                sqlOutput += `INSERT INTO "${table}" (${colNames.join(', ')}) VALUES (${valStrings.join(', ')});\n`;
            }
            console.log(`Successfully exported ${resRows.rows.length} rows.`);
        }

        const dateStr = new Date().toISOString().slice(0, 10).replace(/-/g, '');
        const backupFileName = `unimap360_backup_${dateStr}.sql`;
        const backupPath = path.join(__dirname, '..', backupFileName);
        
        fs.writeFileSync(backupPath, sqlOutput, 'utf8');
        console.log(`\n🎉 SUCCESS: Database backup complete!`);
        console.log(`Saved file to: ${backupPath}`);

        await client.end();
    } catch (err) {
        console.error("\n❌ ERROR: Backup failed!", err.message);
    }
}

backup();
