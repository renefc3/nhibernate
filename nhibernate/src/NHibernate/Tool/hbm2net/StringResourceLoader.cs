/*
* Created on 12-10-2003
*
* To change the template for this generated file go to
* Window - Preferences - Java - Code Generation - Code and Comments
*/
using System;
using ExtendedProperties = Commons.Collections.ExtendedProperties;
using StringInputStream = System.IO.StringReader;
using ResourceNotFoundException = NVelocity.Exception.ResourceNotFoundException;
using Resource = NVelocity.Runtime.Resource.Resource;
using ResourceLoader = NVelocity.Runtime.Resource.Loader.ResourceLoader;
namespace NHibernate.tool.hbm2java
{
	
	/// <author>  MAX
	/// 
	/// To change the template for this generated type comment go to
	/// Window - Preferences - Java - Code Generation - Code and Comments
	/// </author>
	public class StringResourceLoader:ResourceLoader
	{
		
		/* (non-Javadoc)
		* @see org.apache.velocity.runtime.resource.loader.ResourceLoader#init(org.apache.commons.collections.ExtendedProperties)
		*/
		public override void  init(ExtendedProperties configuration)
		{
			// TODO Auto-generated method stub
		}
		
		/* (non-Javadoc)
		* @see org.apache.velocity.runtime.resource.loader.ResourceLoader#getResourceStream(java.lang.String)
		*/
		public override System.IO.Stream getResourceStream(System.String source)
		{
			return new System.IO.MemoryStream(System.Text.Encoding.ASCII.GetBytes(source));
		}
		
		/* (non-Javadoc)
		* @see org.apache.velocity.runtime.resource.loader.ResourceLoader#isSourceModified(org.apache.velocity.runtime.resource.Resource)
		*/
		public override bool isSourceModified(NVelocity.Runtime.Resource.Resource resource)
		{
			return false;
		}
		
		/* (non-Javadoc)
		* @see org.apache.velocity.runtime.resource.loader.ResourceLoader#getLastModified(org.apache.velocity.runtime.resource.Resource)
		*/
		public override long getLastModified(NVelocity.Runtime.Resource.Resource resource)
		{
			//UPGRADE_TODO: Method 'java.lang.System.currentTimeMillis' was converted to 'System.DateTime.Now' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javalangSystemcurrentTimeMillis"'
			return (System.DateTime.Now.Ticks - 621355968000000000) / 10000;
		}
	}
}