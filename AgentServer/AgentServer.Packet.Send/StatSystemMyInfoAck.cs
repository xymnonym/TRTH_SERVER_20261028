using System;
using System.Collections.Generic;
using AgentServer.Network.Connections;
using AgentServer.Structuring;
using AgentServer.Structuring.Opcode;
using LocalCommons.Network;
using MySql.Data.MySqlClient;
using Serilog;

namespace AgentServer.Packet.Send
{
    internal static class StatSystemSp
    {
        public static Dictionary<short, float> LoadActiveBonuses(Account account)
        {
            var bonuses = new Dictionary<short, float>();
            if (account == null) return bonuses;
            try
            {
                using (var conn = new MySqlConnection(Conf.Connstr))
                using (var cmd = new MySqlCommand(@"SELECT d.fdNodeType, n.fdAttrValue
                    FROM userstatsysteminfo s
                    JOIN userstatsystemdetailinfo d ON d.fdUserNum=s.fdUserNum AND d.fdPageNum=s.fdLastSlot
                    JOIN essenstatsystemnodeinfo n ON n.fdAttrType=d.fdNodeType AND n.fdAttrLevel=d.fdSelected
                    WHERE s.fdUserNum=@u AND s.fdLastSlot BETWEEN 1 AND s.fdPageCount
                      AND d.fdNodeType>=0 AND d.fdSelected>0", conn))
                {
                    cmd.Parameters.AddWithValue("@u", account.UserNum);
                    conn.Open();
                    using (var row = cmd.ExecuteReader())
                        while (row.Read())
                        {
                            int attr = row.GetInt32(0);
                            if (attr >= short.MinValue && attr <= short.MaxValue)
                                bonuses[(short)attr] = row.GetFloat(1);
                        }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "StatSystem active bonus load failed userNum={UserNum}", account.UserNum);
            }
            return bonuses;
        }

        public static StatSystemSnapshot Load(Account account)
        {
            if (account == null) throw new ArgumentNullException(nameof(account));
            var info = new StatSystemSnapshot();
            using (var conn = new MySqlConnection(Conf.Connstr))
            {
                conn.Open();
                using (var cmd = new MySqlCommand("INSERT IGNORE INTO userstatsysteminfo (fdUserNum) VALUES (@userNum)", conn))
                {
                    cmd.Parameters.AddWithValue("@userNum", account.UserNum);
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = new MySqlCommand("SELECT fdPageCount, fdRemainPoint, fdLastSlot FROM userstatsysteminfo WHERE fdUserNum=@userNum", conn))
                {
                    cmd.Parameters.AddWithValue("@userNum", account.UserNum);
                    using (var row = cmd.ExecuteReader())
                    {
                        if (!row.Read()) throw new InvalidOperationException("Missing userstatsysteminfo row for user " + account.UserNum);
                        info.PageCount = Math.Max(1, Math.Min(10, row.GetInt32(0)));
                        info.RemainPoint = row.GetInt32(1);
                        info.LastSlot = row.GetInt32(2);
                    }
                }
                using (var cmd = new MySqlCommand("SELECT fdPageNum, fdNodeType, fdSelected, fdTitle FROM userstatsystemdetailinfo WHERE fdUserNum=@userNum ORDER BY fdPageNum, fdNodeType", conn))
                {
                    cmd.Parameters.AddWithValue("@userNum", account.UserNum);
                    using (var row = cmd.ExecuteReader())
                    {
                        while (row.Read())
                        {
                            int page = row.GetInt32(0);
                            int node = row.GetInt32(1);
                            if (node == -1)
                            {
                                string title = row.IsDBNull(3) ? string.Empty : row.GetString(3);
                                if (page >= 1 && page <= info.Titles.Length)
                                    info.Titles[page - 1] = title;
                                else if (page == 0)
                                    info.LegacyFirstTitle = title;
                            }
                            else if (node >= 0 && page >= 1 && page <= info.PageCount)
                            {
                                info.Nodes[page - 1].Add(new KeyValuePair<int, int>(node, row.GetInt32(2)));
                            }
                        }
                    }
                }
                Dictionary<int, int> spent = GetSpentByPage(conn, null, account.UserNum);
                int activePage = info.LastSlot < 1 ? 1 : info.LastSlot;
                int activeSpent;
                spent.TryGetValue(activePage, out activeSpent);
                int budget = checked(info.RemainPoint + activeSpent);
                for (int page = 1; page <= info.PageCount; page++)
                {
                    int pageSpent;
                    spent.TryGetValue(page, out pageSpent);
                    info.PageRemain[page - 1] = Math.Max(0, budget - pageSpent);
                }
                Log.Information("StatSystem page SP userNum={UserNum} activePage={ActivePage} budget={Budget} remaining={Remaining}",
                    account.UserNum, activePage, budget, string.Join(",", info.PageRemain));
            }
            if (string.IsNullOrEmpty(info.Titles[0])) info.Titles[0] = info.LegacyFirstTitle;
            account.StatSystemPageCount = info.PageCount;
            Array.Copy(info.Titles, account.StatSystemTitles, info.Titles.Length);
            int titledPages = 0, selectedNodes = 0;
            for (int page = 0; page < info.PageCount; page++)
            {
                if (!string.IsNullOrEmpty(info.Titles[page])) titledPages++;
                selectedNodes += info.Nodes[page].Count;
            }
            Log.Information("StatSystem load userNum={UserNum} pages={PageCount} lastSlot={LastSlot} remainSp={RemainPoint} titledPages={TitledPages} nodes={Nodes}",
                account.UserNum, info.PageCount, info.LastSlot, info.RemainPoint, titledPages, selectedNodes);
            return info;
        }

        public static void SendMyInfo(ClientConnection client, byte last)
        {
            try
            {
                client.SendAsync(new StatSystemMyInfoAck(Load(client.CurrentAccount), last));
                client.SendAsync(new eServer_GET_USER_CHARACTER_ATTR_ACK(client.CurrentAccount, last));
            }
            catch (Exception ex) { Log.Warning(ex, "StatSystem MyInfo failed for userNum={UserNum}", client.CurrentAccount?.UserNum); }
        }

        private static Dictionary<int, int> GetSpentByPage(MySqlConnection conn, MySqlTransaction tx, int userNum)
        {
            var spent = new Dictionary<int, int>();
            using (var cmd = new MySqlCommand(@"SELECT d.fdPageNum, COALESCE(SUM(n.fdNeedSP),0)
                FROM userstatsystemdetailinfo d
                JOIN essenstatsystemnodeinfo n ON n.fdAttrType=d.fdNodeType AND n.fdAttrLevel<=d.fdSelected
                WHERE d.fdUserNum=@u AND d.fdNodeType>=0 AND d.fdSelected>0
                GROUP BY d.fdPageNum", conn, tx))
            {
                cmd.Parameters.AddWithValue("@u", userNum);
                using (var row = cmd.ExecuteReader())
                    while (row.Read()) spent[row.GetInt32(0)] = Convert.ToInt32(row.GetValue(1));
            }
            return spent;
        }

        public static int SetActivePage(Account account, int page)
        {
            using (var conn = new MySqlConnection(Conf.Connstr))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    int pageCount, remain, lastSlot;
                    using (var cmd = new MySqlCommand("SELECT fdPageCount, fdRemainPoint, fdLastSlot FROM userstatsysteminfo WHERE fdUserNum=@u FOR UPDATE", conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@u", account.UserNum);
                        using (var row = cmd.ExecuteReader())
                        {
                            if (!row.Read()) throw new InvalidOperationException("StatSystem account row missing");
                            pageCount = row.GetInt32(0); remain = row.GetInt32(1); lastSlot = row.GetInt32(2);
                        }
                    }
                    if (page < 1 || page > pageCount || page > 10) throw new InvalidOperationException("StatSystem page is locked: " + page);
                    var spent = GetSpentByPage(conn, tx, account.UserNum);
                    int activeSpent, targetSpent;
                    spent.TryGetValue(lastSlot < 1 ? 1 : lastSlot, out activeSpent);
                    spent.TryGetValue(page, out targetSpent);
                    int pageRemain = checked(remain + activeSpent - targetSpent);
                    if (pageRemain < 0) throw new InvalidOperationException("StatSystem page SP is negative");
                    using (var cmd = new MySqlCommand("UPDATE userstatsysteminfo SET fdLastSlot=@p, fdRemainPoint=@sp WHERE fdUserNum=@u", conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@p", page);
                        cmd.Parameters.AddWithValue("@sp", pageRemain);
                        cmd.Parameters.AddWithValue("@u", account.UserNum);
                        cmd.ExecuteNonQuery();
                    }
                    tx.Commit();
                    Log.Information("StatSystem select page userNum={UserNum} page={Page} remainSp={Remain}", account.UserNum, page, pageRemain);
                    return pageRemain;
                }
            }
        }

        public static int SavePage(Account account, int page, Dictionary<int, int> desired, int claimedRemain)
        {
            if (account == null) throw new ArgumentNullException(nameof(account));
            using (var conn = new MySqlConnection(Conf.Connstr))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    int pageCount, remain, lastSlot;
                    using (var cmd = new MySqlCommand("SELECT fdPageCount, fdRemainPoint, fdLastSlot FROM userstatsysteminfo WHERE fdUserNum=@u FOR UPDATE", conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@u", account.UserNum);
                        using (var row = cmd.ExecuteReader())
                        {
                            if (!row.Read()) throw new InvalidOperationException("StatSystem account row missing");
                            pageCount = row.GetInt32(0);
                            remain = row.GetInt32(1);
                            lastSlot = row.GetInt32(2);
                        }
                    }
            if (page < 1 || page > pageCount || page > 10)
                        throw new InvalidOperationException("StatSystem page is locked: " + page);

                    var old = new Dictionary<int, int>();
                    using (var cmd = new MySqlCommand("SELECT fdNodeType, fdSelected FROM userstatsystemdetailinfo WHERE fdUserNum=@u AND fdPageNum=@p AND fdNodeType>=0 FOR UPDATE", conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@u", account.UserNum);
                        cmd.Parameters.AddWithValue("@p", page);
                        using (var row = cmd.ExecuteReader())
                            while (row.Read()) old[row.GetInt32(0)] = row.GetInt32(1);
                    }

                    var costs = new Dictionary<int, Dictionary<int, int>>();
                    using (var cmd = new MySqlCommand("SELECT fdAttrType, fdAttrLevel, fdNeedSP FROM essenstatsystemnodeinfo ORDER BY fdAttrType, fdAttrLevel", conn, tx))
                    using (var row = cmd.ExecuteReader())
                    {
                        while (row.Read())
                        {
                            int node = row.GetInt32(0), level = row.GetInt32(1);
                            Dictionary<int, int> levels;
                            if (!costs.TryGetValue(node, out levels)) costs[node] = levels = new Dictionary<int, int>();
                            levels[level] = row.GetInt32(2);
                        }
                    }

                    int oldCost = TotalCost(old, costs, false);
                    int newCost = TotalCost(desired, costs, true);
                    var spent = GetSpentByPage(conn, tx, account.UserNum);
                    int activeSpent;
                    spent.TryGetValue(lastSlot < 1 ? 1 : lastSlot, out activeSpent);
                    int newRemain = checked(remain + activeSpent - newCost);
                    if (newRemain < 0 || newRemain != claimedRemain)
                        throw new InvalidOperationException(string.Format("StatSystem SP mismatch page={0} stored={1} oldCost={2} newCost={3} clientRemain={4}", page, remain, oldCost, newCost, claimedRemain));

                    using (var cmd = new MySqlCommand("DELETE FROM userstatsystemdetailinfo WHERE fdUserNum=@u AND fdPageNum=@p AND fdNodeType>=0", conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@u", account.UserNum);
                        cmd.Parameters.AddWithValue("@p", page);
                        cmd.ExecuteNonQuery();
                    }
                    foreach (var entry in desired)
                    {
                        if (entry.Value == 0) continue;
                        using (var cmd = new MySqlCommand("INSERT INTO userstatsystemdetailinfo (fdUserNum, fdPageNum, fdNodeType, fdSelected, fdTitle) VALUES (@u,@p,@n,@s,'')", conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@u", account.UserNum);
                            cmd.Parameters.AddWithValue("@p", page);
                            cmd.Parameters.AddWithValue("@n", entry.Key);
                            cmd.Parameters.AddWithValue("@s", entry.Value);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    if (page == (lastSlot < 1 ? 1 : lastSlot))
                    using (var cmd = new MySqlCommand("UPDATE userstatsysteminfo SET fdRemainPoint=@sp WHERE fdUserNum=@u", conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@sp", newRemain);
                        cmd.Parameters.AddWithValue("@u", account.UserNum);
                        cmd.ExecuteNonQuery();
                    }
                    tx.Commit();
                    Log.Information("StatSystem save userNum={UserNum} page={Page} nodes={NodeCount} oldCost={OldCost} newCost={NewCost} remainSp={RemainPoint}",
                        account.UserNum, page, desired.Count, oldCost, newCost, newRemain);
                    return newRemain;
                }
            }
        }

        private static int TotalCost(Dictionary<int, int> nodes, Dictionary<int, Dictionary<int, int>> costs, bool strict)
        {
            int total = 0;
            foreach (var node in nodes)
            {
                Dictionary<int, int> levels;
                if (node.Value < 0 || !costs.TryGetValue(node.Key, out levels) || node.Value > levels.Count)
                {
                    if (strict) throw new InvalidOperationException("Invalid StatSystem node/level: " + node.Key + "/" + node.Value);
                    continue;
                }
                for (int level = 1; level <= node.Value; level++)
                {
                    int cost;
                    if (!levels.TryGetValue(level, out cost)) throw new InvalidOperationException("Missing StatSystem cost for node " + node.Key + " level " + level);
                    total = checked(total + cost);
                }
            }
            return total;
        }
    }

    internal sealed class StatSystemSnapshot
    {
        public int PageCount;
        public int RemainPoint;
        public readonly int[] PageRemain = new int[10];
        public int LastSlot;
        public string LegacyFirstTitle = string.Empty;
        public readonly string[] Titles = new string[10];
        public readonly List<KeyValuePair<int, int>>[] Nodes = CreateNodes();

        private static List<KeyValuePair<int, int>>[] CreateNodes()
        {
            var result = new List<KeyValuePair<int, int>>[10];
            for (int i = 0; i < result.Length; i++) result[i] = new List<KeyValuePair<int, int>>();
            return result;
        }
    }

    /// <summary>R186632 wire 1989: result, last slot, page map, per-page stat map.</summary>
    public sealed class StatSystemMyInfoAck : NetPacket
    {
        internal StatSystemMyInfoAck(StatSystemSnapshot info, byte last)
        {
            ns.Write((ushort)Opcodes.eServer_STAT_SYSTEM_MYINFO_ACK);
            ns.Write(0); // result
            ns.Write(info.LastSlot);
            ns.Write(info.PageCount);
            for (int page = 0; page < info.PageCount; page++)
            {
                ns.Write(page + 1);
                ns.WriteBIG5Fixed_shortSize(info.Titles[page] ?? string.Empty);
            }
            ns.Write(info.PageCount);
            for (int page = 0; page < info.PageCount; page++)
            {
                ns.Write(page + 1);
                ns.Write(info.Nodes[page].Count);
                foreach (var node in info.Nodes[page])
                {
                    ns.Write(node.Key);
                    ns.Write(node.Value);
                }
                ns.Write(info.PageRemain[page]);
                ns.Write(0);
            }
            _ = last;
        }
    }

    public sealed class StatSystemSetPageNumAck : NetPacket
    {
        public StatSystemSetPageNumAck(int page, byte last)
        {
            ns.WriteOP(Opcodes.eServer_STAT_SYSTEM_SET_PAGENUM_ACK); ns.Write(0); ns.Write(page); ns.Write(0); ns.Write(0); _ = last;
        }
    }

    public sealed class StatSystemPayPageResultAck : NetPacket
    {
        public StatSystemPayPageResultAck(byte last)
        {
            ns.WriteOP(Opcodes.eServer_STAT_SYSTEM_PAY_PAGE_RESULT_ACK); ns.Write(0); ns.Write(0); _ = last;
        }
    }

    public sealed class StatSystemSavePageAck : NetPacket
    {
        internal StatSystemSavePageAck(int page, StatSystemSnapshot info, int selected, byte last)
        {
            ns.WriteOP(Opcodes.eServer_STAT_SYSTEM_SAVE_PAGE_ACK);
            ns.Write(0);
            ns.Write(page);
            ns.Write(info.Nodes[page - 1].Count);
            foreach (var node in info.Nodes[page - 1])
            {
                ns.Write(node.Key);
                ns.Write(node.Value);
            }
            ns.Write(info.PageRemain[page - 1]);
            ns.Write(selected);
            _ = last;
        }
    }

    public sealed class StatSystemSavePageFailedAck : NetPacket
    {
        public StatSystemSavePageFailedAck()
        {
            ns.WriteOP(Opcodes.eServer_STAT_SYSTEM_SAVE_PAGE_ACK);
            ns.Write(1);
        }
    }

    public sealed class StatSystemSaveTitleAck : NetPacket
    {
        public StatSystemSaveTitleAck(int page, string title, byte last)
        {
            ns.WriteOP(Opcodes.eServer_STAT_SYSTEM_SAVE_TITLE_ACK); ns.Write(0); ns.Write(page); ns.WriteBIG5Fixed_shortSize(title ?? string.Empty); _ = last;
        }
    }
}
