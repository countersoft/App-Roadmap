using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Countersoft.Gemini.Commons.Dto;
using Countersoft.Gemini.Commons.Entity;
using Countersoft.Gemini.Commons;
using System.Web.Mvc;

namespace Roadmap
{
    public class Milestone
    {
        public string Category { get; set; }
        public string Label { get; set; }
    }

    public class RoadmapAppModel : Countersoft.Gemini.Models.BaseIssuesModel
    {        
        // labeling and the like - used on al pages
        public string VersionCards { get; set; }
        public string VersionLabel { get; set; }
        public int VersionId { get; set; }

        // Status bar data - used on all pages
        public int Open { get; set; }
        public int Closed { get; set; }
        public int Total { get { return Open + Closed; } }
        public IEnumerable<Triple<string, int, string>> Statuses { get; set; }
        public IEnumerable<Triple<string, int, string>> Types { get; set; }
        public IEnumerable<Pair<string, int>> Resources { get; set; }

        public CountTypes ItemHours { get; set; }
        public int Days { get; set; }
        public DateTime? ReleaseStartDate { get; set; }
        public DateTime? ReleaseEndDate { get; set; }
        public DateTime? EstimatedEndDate { get; set; }
        public int TimeLoggedOver { get; set; }
        public MultiSelectList StatusAsClosed { get; set; }

        // Various Charts
        public List<VersionBurndown> Burndown { get; set; }
        public List<VersionBurnup> Burnup { get; set; }
        public List<VersionVelocity> Velocity { get; set; }
        public List<double> IdealLine { get; set; }
        public List<double?> BurndownTrendline { get; set; }
        public List<double?> BurnupTrendline { get; set; }
        public List<double?> VelocityLine { get; set; }
        public List<Milestone> Milestones { get; set; }

        public IEnumerable<SelectListItem> ProjectList { get; set; }

        public ProjectTemplatePageType Type
        {
            get
            {
                return PageType;
            }
            set
            {
                PageType = value;
            }
        }
        
        // Resource Work Breakdown
        public List<VersionWorkBreakdown> ResourceBreakdown { get; set; }

        public RoadmapAppModel()
        {
            Burndown = new List<VersionBurndown>();
            Burnup = new List<VersionBurnup>();
            Velocity = new List<VersionVelocity>();
            IdealLine = new List<double>();
            BurndownTrendline = new List<double?>();
            BurnupTrendline = new List<double?>();
            VelocityLine = new List<double?>();

            Types = new List<Triple<string, int, string>>();
            Statuses = new List<Triple<string, int, string>>();
            Resources = new List<Pair<string, int>>();
        }

        public string Sort { get; set; }

        public int Estimated { get; set; }

        public int Logged { get; set; }

        public int Remain { get; set; }
    }

    public class PageSettings : IssuesGridFilter
    {
        public PageData PageData { get; set; }

        public PageSettings()
        {
            PageData = new PageData();
        }
    }

    public class PageData
    {
        public int versionId { get; set; }
        public int projectId { get; set; }
    }
}