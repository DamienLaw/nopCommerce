﻿using System;
using Nop.Web.Framework.Models;

namespace Nop.Web.Models.Cms
{
    public partial record RenderWidgetModel : BaseNopModel
    {
        public Type WidgetViewComponentType { get; set; }
        public object WidgetViewComponentArguments { get; set; }
    }
}