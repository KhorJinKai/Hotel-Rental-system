namespace HouseRentalSystem.Models
{
    // Route-value helpers for the search page. These used to live as @functions inside
    // Index.cshtml; they were lifted out so the results partial (_Results.cshtml) can
    // build the same links - sort tabs, filter-tag removers, score pills and the new
    // pagination all need "the current search, with one thing changed".
    public static class SearchRoute
    {
        // The whole current search as route values. "recommended" sort and page 1 are
        // the defaults, so they are left out to keep shared URLs tidy.
        public static Dictionary<string, string> Current(HomeSearchViewModel m)
        {
            var route = new Dictionary<string, string>();

            if (!string.IsNullOrWhiteSpace(m.Location)) route["location"] = m.Location;
            if (m.MoveInDate.HasValue) route["moveInDate"] = m.MoveInDate.Value.ToString("yyyy-MM-dd");
            if (m.CategoryID.HasValue) route["categoryID"] = m.CategoryID.Value.ToString();
            if (m.MinPrice.HasValue) route["minPrice"] = m.MinPrice.Value.ToString("0");
            if (m.MaxPrice.HasValue) route["maxPrice"] = m.MaxPrice.Value.ToString("0");
            if (m.Bedrooms.HasValue) route["bedrooms"] = m.Bedrooms.Value.ToString();
            if (m.MinScore.HasValue) route["minScore"] = m.MinScore.Value.ToString();
            if (!string.IsNullOrWhiteSpace(m.SortBy) && m.SortBy != "recommended") route["sortBy"] = m.SortBy;

            var ids = m.AmenityIDs ?? System.Array.Empty<int>();
            for (int i = 0; i < ids.Length; i++)
            {
                route[$"amenityIDs[{i}]"] = ids[i].ToString();
            }

            return route;
        }

        // Same search with one value swapped in, or removed when value is null.
        // Changing any filter or the sort resets paging, so page is never carried here.
        public static Dictionary<string, string> With(HomeSearchViewModel m, string key, string value)
        {
            var route = Current(m);
            if (value == null) route.Remove(key);
            else route[key] = value;
            return route;
        }

        public static Dictionary<string, string> Without(HomeSearchViewModel m, string keyA, string keyB)
        {
            var route = Current(m);
            route.Remove(keyA);
            route.Remove(keyB);
            return route;
        }

        // Same search with the facility list replaced wholesale.
        public static Dictionary<string, string> WithAmenities(HomeSearchViewModel m, int[] amenityIds)
        {
            var route = Current(m);

            var existing = m.AmenityIDs ?? System.Array.Empty<int>();
            for (int i = 0; i < existing.Length; i++)
            {
                route.Remove($"amenityIDs[{i}]");
            }
            for (int i = 0; i < amenityIds.Length; i++)
            {
                route[$"amenityIDs[{i}]"] = amenityIds[i].ToString();
            }

            return route;
        }

        // Current search pinned to a specific results page (used only by the pagination nav).
        public static Dictionary<string, string> WithPage(HomeSearchViewModel m, int page)
        {
            var route = Current(m);
            if (page > 1) route["page"] = page.ToString();
            else route.Remove("page");
            return route;
        }
    }
}
