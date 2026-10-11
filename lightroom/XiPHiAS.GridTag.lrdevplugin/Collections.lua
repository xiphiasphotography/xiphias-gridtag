local Collections = {}

local function find(catalog, name)
    for _, value in ipairs(catalog:getChildCollections()) do
        if value:getName() == name then return value end
    end
end

function Collections.prepare(catalog)
    local reviewName, noCarName = "GridTag Review", "GridTag GeenAuto"
    if not find(catalog, reviewName) or not find(catalog, noCarName) then
        catalog:withWriteAccessDo("GridTag collecties aanmaken", function()
            if not find(catalog, reviewName) then catalog:createCollection(reviewName, nil, true) end
            if not find(catalog, noCarName) then catalog:createCollection(noCarName, nil, true) end
        end)
    end
    -- New collections become accessible only after the creation gate commits.
    local review, noCar = find(catalog, reviewName), find(catalog, noCarName)
    if not review or not noCar then error("GridTag-collecties konden niet worden aangemaakt.") end
    return { review = review, noCar = noCar }
end

function Collections.apply(catalog, photo, status, prepared)
    local review, noCar = prepared.review, prepared.noCar
    if status == "review" or status == "error" then
        review:addPhotos({ photo })
        noCar:removePhotos({ photo })
    elseif status == "noCar" then
        noCar:addPhotos({ photo })
        review:removePhotos({ photo })
    elseif status == "auto" or status == "manual" then
        review:removePhotos({ photo })
        noCar:removePhotos({ photo })
    end
end

return Collections
