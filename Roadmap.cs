using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Web.Mvc;
using System.Web.Routing;
using System.Web.UI;
using Countersoft.Foundation.Commons.Extensions;
using Countersoft.Gemini.Commons;
using Countersoft.Gemini.Commons.Dto;
using Countersoft.Gemini.Commons.Entity;
using Countersoft.Gemini.Commons.Permissions;
using Countersoft.Gemini.Extensibility.Apps;
using Countersoft.Gemini.Infrastructure;
using Countersoft.Gemini.Infrastructure.Apps;
using Countersoft.Gemini.Models;
using System.Linq;
using System.Text;
using Countersoft.Gemini.Infrastructure.Helpers;
using Countersoft.Foundation.Commons.Enums;
using Countersoft.Gemini.Commons.Entity.Security;
using Countersoft.Gemini.Commons.Meta;
using NHibernate;

namespace Roadmap
{
    internal static class Constants
    {
        public static string AppId = "1F21A63F-94FF-46D0-8773-9E482EF0CA90";
        public static string ControlId = "9E2D9244-8F2A-477A-8584-9EE39DE18C6D";
        public static string RoadmapSessionView = "RoadmapSessionView";
    }

    [AppType(AppTypeEnum.FullPage),
    AppGuid("1F21A63F-94FF-46D0-8773-9E482EF0CA90"),
    AppControlGuid("9E2D9244-8F2A-477A-8584-9EE39DE18C6D"),
    AppAuthor("Countersoft"),
    AppKey("roadmap"),
    AppName("Roadmap"),
    AppDescription("Roadmap"),
    AppControlUrl("view")]
    [OutputCache(Duration = 0, NoStore = false, Location = OutputCacheLocation.None)]
    public class PageRoadmap : BaseAppController
    {
        public override WidgetResult Show(IssueDto issue = null)
        {
            var filter = IsSessionFilter() || CurrentCard.CardType != ProjectTemplatePageType.Custom && "app/roadmap/view".Equals(CurrentCard.Url, StringComparison.InvariantCultureIgnoreCase) ? HttpSessionManager.GetFilter(CurrentCard.Id, IssuesFilter.CreateProjectFilter(CurrentUser.Entity.Id, CurrentProject.Entity.Id)) : CurrentCard.Filter;
            HttpSessionManager.SetFilter(CurrentCard.Id, filter);
            var workspaceProjects = new List<int>();
            
            int? currentProjectId = 0;
            bool includeSubversions = false;
            
            int versionId = 0;

            HttpSessionManager.Set<List<UserIssuesView>>(null, Constants.RoadmapSessionView);

            // Safety check required because of http://gemini.countersoft.com/project/DEV/21/item/5088
            PageSettings pageSettings = null;
            
            try
            {
                if(CurrentCard.Options.ContainsKey(AppGuid))
                {
                    pageSettings = CurrentCard.Options[AppGuid].FromJson<PageSettings>();

                    if (pageSettings.PageData != null)
                    {
                        currentProjectId = pageSettings.PageData.projectId;
                        
                        versionId = pageSettings.PageData.versionId;
                        includeSubversions = pageSettings.PageData.includeSubversions;
                    }
                }
            }
            catch (Exception ex){}

            var activeProjects = ProjectManager.GetActive();
            
            var viewableProjects = new List<ProjectDto>();

            if (activeProjects == null || activeProjects.Count == 0)
            {
                activeProjects = new List<ProjectDto>();
            }
            else
            {
                viewableProjects = ProjectManager.GetAppViewableProjects(this);
            }

            if (!viewableProjects.Any(s => s.Entity.Id == currentProjectId.Value))
            {
                currentProjectId = viewableProjects.Count > 0 ? viewableProjects.First().Entity.Id : 0;
            }

            UserContext.Project = ProjectManager.Get(currentProjectId);

            IEnumerable<Countersoft.Gemini.Commons.Entity.Version> versions = null;

            // get all versions that are released or not according to settings passed (BUT - NEVER show archived projects)
            versions = Cache.Versions.GetAll().Where(v => v.ProjectId == currentProjectId && v.Released == false && v.Archived == false).OrderBy(o => o.Sequence);

            if (versionId == 0)
            {
                VersionDto version = VersionManager.GetFirstRoadmapVersion(UserContext.Project.Entity.Id);

                if (version != null)
                {
                    versionId = version.Entity.Id;
                }
                else
                {
                    versionId = versions.Count() > 0 ? versions.First().Id : 0;
                }
            }

            List<IssueDto> issues;
            issues = IssueManager.GetRoadmap(UserContext.Project.Entity.Id, filter, versionId);
            if (includeSubversions)
            {
                var child = versions.Where(v => v.ParentId == versionId).ToList();
                AddChildVersions(versions.ToList(), UserContext.Project.Entity.Id, filter, child, ref issues);
            }
            
            

            RoadmapAppModel model = BuildModelData(versionId, versions, issues);
            
            model.ProjectList = new SelectList(viewableProjects, "Entity.Id", "Entity.Name", currentProjectId.GetValueOrDefault());
            model.IncludeSubVersions = includeSubversions;

            if(pageSettings == null)
            {
                pageSettings = new PageSettings();
            }

            pageSettings.PageData.versionId = versionId;
            
            pageSettings.PageData.projectId = currentProjectId.GetValueOrDefault();

            CurrentCard.Options[AppGuid] = pageSettings.ToJson();

            //var view = RenderPartialViewToString(this, "~/views/Roadmap.cshtml", model);
            return new WidgetResult() { Success = true, Markup = new WidgetMarkup("views/Roadmap.cshtml", model) };
        }

        public override WidgetResult Caption(IssueDto issue = null)
        {
            return new WidgetResult() { Success = true, Markup = new WidgetMarkup("Roadmap") };
        }

        private RoadmapAppModel BuildModelData(int versionId, IEnumerable<Countersoft.Gemini.Commons.Entity.Version> iVersions, List<IssueDto> issues, IssuesFilter OriginalFilter = null)
        {
            StringBuilder builder = new StringBuilder();
            
            RoadmapAppModel model = new RoadmapAppModel();//GetModel<RoadmapAppModel>();

            List<VersionDto> versions = VersionManager.Convert(new List<Countersoft.Gemini.Commons.Entity.Version>(iVersions));

            if (versions.Count != iVersions.Count())
            {
                // Need to get the version again as parents is missing, probably released 
                foreach (var ver in iVersions)
                {
                    if (versions.Find(v => v.Entity.Id == ver.Id) == null)
                    {
                        int index = versions.FindIndex(v => v.Entity.Sequence > ver.Sequence);

                        if (index == -1)
                        {
                            versions.Add(VersionManager.Convert(ver));
                        }
                        else
                        {
                            versions.Insert(index, VersionManager.Convert(ver));
                        }
                    }
                }
            }

            IssuesFilter filter = new IssuesFilter();

            // Build up the progress data on all the cards as we go
            foreach (var version in versions)
            {
                RoadmapAppModel tmp = new RoadmapAppModel();

                builder.Append(BuildAllProjectVersions(version.Entity, version.Entity.Id == versionId, version.HierarchyLevel));

                if (version.Entity.Id == versionId)
                {
                    var visibility = GetRoadmapFields(UserContext.Project.Entity.Id);

                    var properties = GridManager.GetDisplayProperties(MetaManager.TypeGetAll(new List<ProjectDto>() { CurrentProject }), visibility, new List<int>() { version.Project.Entity.Id });

                    List<ColumnInfoModel> gridColumns = GridManager.DescribeGridColumns(properties);
                    
                    GridOptionsModel gridOptions = GridManager.DescribeGridOptions();

                    // get the version specific data
                    tmp.Issues = issues;
                    
                    tmp.Columns = gridColumns;
                    
                    tmp.Options = gridOptions;
                    
                    tmp.Closed = tmp.Issues.Count(i => i.IsClosed);
                    
                    tmp.Open = tmp.Issues.Count(i => !i.IsClosed);
                    
                    tmp.Statuses = (from i in tmp.Issues group i by new { Id = i.Entity.StatusId, Name = i.Status } into g select new Triple<string, int, string>(g.Key.Name, g.Count(), string.Format("{0}{1}?versions={2}&statuses={3}", UserContext.Url, NavigationHelper.GetProjectPageUrl(UserContext.Project, ProjectTemplatePageType.Items), versionId, g.Key.Id))).OrderByDescending(g => g.Second).Take(3);
                    
                    tmp.Types = (from i in tmp.Issues group i by new { Id = i.Entity.TypeId, Name = i.Type } into g select new Triple<string, int, string>(g.Key.Name, g.Count(), string.Format("{0}{1}?versions={2}&types={3}&includeclosed=yes", UserContext.Url, NavigationHelper.GetProjectPageUrl(UserContext.Project, ProjectTemplatePageType.Items), versionId, g.Key.Id))).OrderByDescending(g => g.Second).Take(3);

                    // store the version Id 
                    model.VersionId = version.Entity.Id;
                    
                    model.VersionLabel = version.Entity.Name;
                    
                    model.Closed = tmp.Closed;
                    
                    model.Columns = tmp.Columns;
                    
                    model.Issues = tmp.Issues;
                    
                    model.DisplayData = GridManager.GetDisplayData(tmp.Issues, tmp.Columns);
                    
                    model.Open = tmp.Open;
                    
                    model.Options = tmp.Options;
                    
                    model.Statuses = tmp.Statuses;
                    
                    model.Types = tmp.Types;
                    
                    model.Estimated = issues.Sum(i => i.Entity.EstimatedHours * 60 + i.EstimatedMinutes);
                    
                    model.Logged = issues.Sum(i => i.Entity.LoggedHours * 60 + i.Entity.LoggedMinutes);
                    
                    model.Remain = model.Estimated > model.Logged ? model.Estimated - model.Logged : 0;
                    
                    model.TimeLoggedOver = model.Logged > model.Estimated ? model.Logged - model.Estimated : 0;
                    
                    model.ReleaseStartDate = version.Entity.StartDate;
                    
                    model.ReleaseEndDate = version.Entity.ReleaseDate;

                    // Now get with the extra data that we need
                    var projectId = UserContext.Project.Entity.Id;

                    IssuesGridFilter defaultFilter = new IssuesGridFilter(IssuesFilter.CreateProjectFilter(CurrentUser.Entity.Id, CurrentProject.Entity.Id));
                    
                    filter = IssuesFilter.CreateVersionFilter(CurrentUser.Entity.Id, CurrentProject.Entity.Id, version.Entity.Id);

                    if (OriginalFilter != null)
                    {
                        filter.SortField = OriginalFilter.SortField;
                        filter.SortOrder = OriginalFilter.SortOrder;
                    }

                    ItemFilterManager.SetSortedColumns(gridColumns, filter);
                    model.Filter = IssueFilterHelper.PopulateModel(model.Filter, filter, filter, PermissionsManager, ItemFilterManager, IssueFilterHelper.GetViewableFields(filter, ProjectManager, MetaManager), false);
                }
            }

            // attach version cards
            model.VersionCards = builder.ToString();

            // Visibility
            model[ItemAttributeVisibility.Status] = CanSeeProjectItemAttribute(ItemAttributeVisibility.Status);
            
            model[ItemAttributeVisibility.EstimatedEffort] = CanSeeProjectItemAttribute(ItemAttributeVisibility.EstimatedEffort);
            
            model[ItemAttributeVisibility.CalculatedTimeLogged] = CanSeeProjectItemAttribute(ItemAttributeVisibility.CalculatedTimeLogged);
            
            model[ItemAttributeVisibility.CalculatedTimeRemaining] = model[ItemAttributeVisibility.EstimatedEffort] && model[ItemAttributeVisibility.CalculatedTimeLogged];

            StringBuilder sort = new StringBuilder();
            
            List<string> sorting = new IssuesFilter().GetSortFields();
            
            List<int> orders = new IssuesFilter().GetSortOrders();

            for (int i = 0; i < sorting.Count; i++)
            {
                sort.AppendFormat("{0}|{1}", sorting[i], orders[i] == 1 ? 0 : 1);
            }

            model.Sort = sort.ToString();
            
            model.CurrentPageCard = CurrentCard;

            return model;
        }

        private string BuildAllProjectVersions(Countersoft.Gemini.Commons.Entity.Version version, bool selected, int level)
        {
            StringBuilder builder = new StringBuilder();

            // Encode all the characters in the string correctly (i.e. not with a '+' instead of '%20') for a space
            var label = System.Uri.EscapeDataString(version.Name);

            builder.Append(selected ? "<li class='selected'>" : "<li>");

            builder.AppendFormat("<a href='{0}workspace/{1}/apps/roadmap/getroadmap?versionId={2}&projectId={3}'>", UserContext.Url, CurrentCard.Id, version.Id, CurrentProject.Entity.Id);

            builder.AppendFormat("<div data-id='{0}' class='version-box' style='margin-left:{1}px'>", version.Id, level * 10);
            
            builder.AppendFormat("<span class='title'>{0}</span>", version.Name);
            
            builder.Append("</div></a></li>");

            return builder.ToString();
        }

        [AppUrl("getroadmap")]
        public ActionResult GetRoadmap(int versionId, int projectId, bool includeSubVersions)
        {
            UserContext.Project = ProjectManager.Get(projectId);

            IssuesFilter filter = null;
            
            if (Request.Form.Keys.Count > 0)
            {
                filter = new IssuesFilter();
            
                string[] sort = Request.Form[0].Split(new char[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                
                StringBuilder sortField = new StringBuilder();
                
                StringBuilder sortDirection = new StringBuilder();

                for (int i = 0; i < sort.Length; i += 2)
                {
                    sortField.Append(sort[i]);
                  
                    sortField.Append('|');
                    
                    sortDirection.Append(sort[i + 1] == "0" ? (int)SortDirection.Ascending : (int)SortDirection.Descending);
                    
                    sortDirection.Append('|');
                }

                filter.SortField = sortField.ToString();
               
                filter.SortOrder = sortDirection.ToString();
                
                filter.ShowSequenced = false;
            }

            if (filter == null)
            {
                filter = HttpSessionManager.GetFilter(CurrentCard.Id, new IssuesFilter());
                
                filter.ShowSequenced = false;
            }

            IEnumerable<Countersoft.Gemini.Commons.Entity.Version> versions = Cache.Versions.GetAll().Where(v => v.ProjectId == projectId && v.Released == false && v.Archived == false).OrderBy(o => o.Sequence);

            // get all versions that are released or not according to settings passed (BUT - NEVER show archived projects)
            if (!versions.Any(s => s.Id == versionId))
            {
                VersionDto newVersion = VersionManager.GetFirstRoadmapVersion(UserContext.Project.Entity.Id);

                if (newVersion != null)
                {
                    versionId = newVersion.Entity.Id;
                }
                else
                {
                    versionId = versions.Count() > 0 ? versions.First().Id : 0;
                }
            }

            var version = VersionManager.Get(versionId);
            List<IssueDto> issues = new List<IssueDto>();

            if (version != null && !version.Entity.Released)
            {
                issues = IssueManager.GetRoadmap(UserContext.Project.Entity.Id, filter, versionId);
                if (includeSubVersions)
                {
                    var child = versions.Where(v => v.ParentId == version.Entity.Id).ToList();
                    AddChildVersions(versions.ToList(), UserContext.Project.Entity.Id, filter, child, ref issues);
                }
            }

            RoadmapAppModel model = BuildModelData(versionId, versions, issues, filter);
            model.IncludeSubVersions = includeSubVersions;
            model.Issues = issues;

            return JsonSuccess(new { success = true, grid = RenderPartialViewToString(this, "~/Views/Shared/DisplayTemplates/IssueDto.cshtml", model), statusBar = RenderPartialViewToString(this, AppManager.Instance.GetAppUrl("1F21A63F-94FF-46D0-8773-9E482EF0CA90", "views/StatusBar.cshtml"), model), versions = RenderPartialViewToString(this, AppManager.Instance.GetAppUrl("1F21A63F-94FF-46D0-8773-9E482EF0CA90", "views/VersionProgress.cshtml"), model) });
        }

        private void AddChildVersions(List<Countersoft.Gemini.Commons.Entity.Version> versions, int projectId, IssuesFilter filter, List<Countersoft.Gemini.Commons.Entity.Version> child, ref List<IssueDto> issues)
        {
            foreach (var version in child)
            {
                issues.AddRange(IssueManager.GetRoadmap(projectId, filter, version.Id));
                AddChildVersions(versions, projectId, filter, versions.FindAll(v => v.ParentId == version.Id), ref issues);
            }
        }

        [AppUrl("getissuerow")]
        public ActionResult GetIssueRow(int issueId)
        {
            var issue = IssueManager.Get(issueId);

            ItemsGridModel model = new ItemsGridModel();
            
            var visibility = GetRoadmapFields(issue.Entity.ProjectId);

            List<int> projectIds = new List<int>();
            
            projectIds.Add(issue.Entity.ProjectId);

            var properties = GridManager.GetDisplayProperties(MetaManager.TypeGetAll(new List<ProjectDto>() { UserContext.Project }), visibility, projectIds);

            model.AllowSequencing = false;
            
            model.ShowSequencing = false;
            
            model.GroupDependencies = false;
            
            model.Issues.Add(issue);
            
            model.Columns = GridManager.DescribeGridColumns(properties);
            
            model.DisplayData = GridManager.GetDisplayData(model.Issues, model.Columns);
            
            model.Options = GridManager.DescribeGridOptions();

            JsonResponse response = new JsonResponse();
            
            response.Success = true;
            
            response.Result = new
            {
                Html = RenderPartialViewToString(this, "~/Views/Shared/DisplayTemplates/IssueDtoRow.cshtml", model),
            };

            string data = response.ToJson().Replace("<", "\\u003c").Replace(">", "\\u003e").Replace("&", "\\u0026");

            return Content(data, Request.Files.Count == 0 ? "application/json" : "text/html");
        }

        private List<ScreenField> GetRoadmapFields(int projectId)
        {
            List<UserIssuesView> roadmapView = null;

            roadmapView = HttpSessionManager.Get<List<UserIssuesView>>(Constants.RoadmapSessionView, null);

            if (roadmapView == null && CurrentCard.Options.ContainsKey(AppGuid))
            {
                var view = CurrentCard.Options[AppGuid].FromJson<PageSettings>();

                if (view.DisplayColumns.Count > 0)
                {
                    roadmapView = view.DisplayColumns;
                }
            }

            if (roadmapView != null)
            {
                return GridManager.GetUserView(roadmapView);
            }
            else
            {
                return GridManager.GetUserView(ProjectTemplatePageType.Custom);
            }
        }

        [AppUrl("getcolumns")]
        public ActionResult GetColumns(int projectId)
        {
            ColumnsModel model = new ColumnsModel();

            List<ScreenField> fields = GetRoadmapFields(projectId);

            IssuesFilter filter = ItemFilterManager.TransformFilter(IssuesFilter.CreateProjectFilter(CurrentUser.Entity.Id, projectId));
            
            SetCurrentProjectFromFilter(filter);

            model.Columns = GridManager.GetAvailableColumns(filter.GetProjects(), IssueFilterHelper.AggregateTypes(filter, ProjectManager, MetaManager), fields);

            return JsonSuccess(RenderPartialViewToString(this, "~/Views/Items/ColumnSelector.cshtml", model));
        }


        [AppUrl("setcolumns")]
        public ActionResult SetColumns(ColumnsModel columnsModel, int projectId, int versionId)
        {
            List<ListItem> selected = columnsModel.Columns.FindAll(c => c.IsSelected);
            
            List<UserIssuesView> currentView = GridManager.GetUserViewColumns(ProjectTemplatePageType.Custom);
            
            int sequence = 0;

            // Remove all properties that are not in the new view.
            currentView.RemoveAll(v => !v.IsCustomField && selected.Find(s => s.ItemId == ((int)v.Attribute).ToString()) == null);
            
            currentView.RemoveAll(v => v.IsCustomField && selected.Find(s => s.ItemId == v.CustomFieldId) == null);
            
            var extra = selected.FindAll(s => currentView.Find(v => !v.IsCustomField && s.ItemId == ((int)v.Attribute).ToString() || v.IsCustomField && s.ItemId == v.CustomFieldId) == null);
            
            foreach (var selectedCol in extra)
            {
                UserIssuesView column = new UserIssuesView();
            
                column.UserId = CurrentUser.Entity.Id;
                
                column.ProjectId = projectId;
                
                column.Sequence = 999;
                
                column.ViewType = ProjectTemplatePageType.Custom;
                
                if (selectedCol.ItemId.StartsWith("cf_", StringComparison.InvariantCulture))
                {
                    column.CustomFieldId = selectedCol.ItemId;
                }
                else
                {
                    column.Attribute = (ItemAttributeVisibility)selectedCol.ItemId.ToInt();
                }

                currentView.Add(column);
            }

            currentView.ForEach(v => { v.Sequence = sequence++; v.UserId = CurrentUser.Entity.Id; });

            HttpSessionManager.Set(currentView, Constants.RoadmapSessionView);

            return JsonSuccess(new { view = currentView });
        }

        [AppUrl("reordercolumns")]
        public ActionResult ReorderColumns(int projectId, int versionId, string from, string to)
        {
            var userView = HttpSessionManager.Get<List<UserIssuesView>>(Constants.RoadmapSessionView, null);

            if (userView == null)
            {
                userView = GridManager.GetUserViewColumns(ProjectTemplatePageType.Custom);
            }

            var view = GridManager.ReorderUserView(from, to, ProjectTemplatePageType.Custom, userView);

            HttpSessionManager.Set(view, Constants.RoadmapSessionView);

            return JsonSuccess(view);
        }
    }
}
