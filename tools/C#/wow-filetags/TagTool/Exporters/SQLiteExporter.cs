using Microsoft.Data.Sqlite;
using WoWTagLib.DataSources;
namespace TagTool.Exporters
{
    public class SQLiteExporter(Repository repo)
    {
        private Repository _repo = repo;

        public void Export(string outputPath)
        {
            // Delete existing file if it exists
            if (File.Exists(outputPath))
                File.Delete(outputPath);

            using (var connection = new SqliteConnection($"Data Source={outputPath}"))
            {
                connection.Open();

                // wow_tags
                var createCmd = new SqliteCommand("CREATE TABLE IF NOT EXISTS wow_tags (id INTEGER PRIMARY KEY AUTOINCREMENT, key TEXT, name TEXT, description TEXT, type TEXT, source TEXT, category TEXT, allow_multiple INTEGER, status TEXT)", connection);
                createCmd.ExecuteNonQuery();

                var indexCmd = new SqliteCommand("CREATE UNIQUE INDEX IF NOT EXISTS wow_tags_idx ON wow_tags (key)", connection);
                indexCmd.ExecuteNonQuery();

                var insertCmd = new SqliteCommand("INSERT OR REPLACE INTO wow_tags (key, name, description, type, source, category, allow_multiple, status) VALUES (@key, @name, @description, @type, @source, @category, @allow_multiple, @status)", connection);

                foreach (var tag in _repo.GetTags())
                {
                    insertCmd.Parameters.Clear();
                    insertCmd.Parameters.AddWithValue("@key", tag.Key);
                    insertCmd.Parameters.AddWithValue("@name", tag.Name);
                    insertCmd.Parameters.AddWithValue("@description", tag.Description);
                    insertCmd.Parameters.AddWithValue("@type", tag.Type.ToString());
                    insertCmd.Parameters.AddWithValue("@source", tag.Source.ToString());
                    insertCmd.Parameters.AddWithValue("@category", tag.Category);
                    insertCmd.Parameters.AddWithValue("@allow_multiple", tag.AllowMultiple ? 1 : 0);
                    insertCmd.Parameters.AddWithValue("@status", tag.Status.ToString());
                    insertCmd.ExecuteNonQuery();
                }

                var tagKeyToID = new Dictionary<string, int>();
                var selectCmd = new SqliteCommand("SELECT id, key FROM wow_tags", connection);
                selectCmd.ExecuteNonQuery();

                using (var reader = selectCmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int id = reader.GetInt32(0);
                        string key = reader.GetString(1);
                        tagKeyToID[key] = id;
                    }
                }

                // wow_tag_presets
                createCmd = new SqliteCommand("CREATE TABLE IF NOT EXISTS wow_tag_presets (id INTEGER PRIMARY KEY AUTOINCREMENT, tag_key TEXT, option TEXT, description TEXT, aliases TEXT)", connection);
                createCmd.ExecuteNonQuery();

                insertCmd = new SqliteCommand("INSERT INTO wow_tag_presets (tag_key, option, description, aliases) VALUES (@tag_key, @option, @description, @aliases)", connection);
                foreach (var tag in _repo.GetTags())
                {
                    foreach (var preset in tag.Presets)
                    {
                        insertCmd.Parameters.Clear();
                        insertCmd.Parameters.AddWithValue("@tag_key", tag.Key);
                        insertCmd.Parameters.AddWithValue("@option", preset.Option);
                        insertCmd.Parameters.AddWithValue("@description", preset.Description);
                        insertCmd.Parameters.AddWithValue("@aliases", string.Join(",", preset.Aliases));
                        insertCmd.ExecuteNonQuery();
                    }
                }

                // wow_tag_mappings, (filedataid, tag_id, value) allow multiple mappings per filedataid and tag
                createCmd = new SqliteCommand("CREATE TABLE IF NOT EXISTS wow_tag_mappings (id INTEGER PRIMARY KEY AUTOINCREMENT, filedataid INTEGER, tag_id INTEGER, value TEXT)", connection);
                createCmd.ExecuteNonQuery();

                insertCmd = new SqliteCommand("INSERT INTO wow_tag_mappings (filedataid, tag_id, value) VALUES (@filedataid, @tag_id, @value)", connection);

                using (var transaction = connection.BeginTransaction())
                {
                    insertCmd.Transaction = transaction;

                    foreach (var mappings in _repo.FileDataIDMap)
                    {
                        insertCmd.Parameters.AddWithValue("@filedataid", mappings.Key);
                        insertCmd.Parameters.AddWithValue("@tag_id", 0);
                        insertCmd.Parameters.AddWithValue("@value", "");

                        foreach (var mapping in mappings.Value)
                        {
                            insertCmd.Parameters["@tag_id"].Value = tagKeyToID[mapping.Tag];
                            insertCmd.Parameters["@value"].Value = mapping.TagValue;
                            insertCmd.ExecuteNonQuery();
                        }

                        insertCmd.Parameters.Clear();
                    }

                    transaction.Commit();
                }
            }
        }
    }
}
