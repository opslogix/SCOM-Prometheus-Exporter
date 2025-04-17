using Microsoft.EnterpriseManagement;
using Microsoft.EnterpriseManagement.Common;
using System;

namespace SCOM.Exporter.Utils
{
    public static class Extensions
    {
        public static T WithReconnect<T>(this ManagementGroup mg, Func<T> action)
        {
            try
            {
                return action();
            }
            catch (ServerDisconnectedException)
            {
                mg.Reconnect();
                return action();
            }
        }
    }
}
