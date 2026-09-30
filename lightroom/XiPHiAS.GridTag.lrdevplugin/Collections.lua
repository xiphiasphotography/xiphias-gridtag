local Collections = {}

local function collection(catalog, name)
    return catalog:createCollection(name, nil, true)
end

function Collections.apply(catalog, photo, status)
    local review = collection(catalog, "GridTag Review")
    local noCar = collection(catalog, "GridTag GeenAuto")
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
