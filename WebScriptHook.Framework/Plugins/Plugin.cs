﻿namespace WebScriptHook.Framework.Plugins
{
    /// <summary>
    /// A plugin extends the request types WebScriptHook can handle
    /// </summary>
    public abstract class Plugin
    {
        protected internal virtual string PluginIDImpl
        {
            get
            {
                // This allows internal builtin plugins to override their ID string
                return this.GetType().Assembly.GetName().Name + "." + this.GetType().FullName;
            }
        }

        /// <summary>
        /// Gets the callable ID of the plugin. ID string is 
        /// in the format of assemblyname.namespace.classname
        /// </summary>
        public string PluginID
        {
            get { return PluginIDImpl; }
        }

        /// <summary>
        /// Dispatch another plugin loaded in Plugin Manager
        /// </summary>
        /// <param name="targetID">Callee's plugin ID</param>
        /// <param name="args">Arguments passed with the call</param>
        /// <returns>Object returned by the callee</returns>
        protected object Dispatch(string targetID, object[] args)
        {
            return PluginManager.Instance.Dispatch(targetID, args);
        }
        

        /// <summary>
        /// Requests the key-value pair be cached in this plugin's cache map, on the server
        /// </summary>
        /// <param name="key">Key of the entry in the cache map</param>
        /// <param name="value">Value of the entry in the cache map</param>
        protected void SetCache(string key, object value)
        {
            PluginManager.Instance.SetCache(PluginID, key, value);
        }
    }
}