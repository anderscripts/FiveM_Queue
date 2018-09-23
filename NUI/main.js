function ClosePanel()
{
	var obj = { message: 'closing panel' };
	$.post('http://fivemqueue/ClosePanel', JSON.stringify(obj));
}

function RefreshPanel()
{
	var obj = { message: 'refreshing panel' };
	$.post('http://fivemqueue/RefreshPanel', JSON.stringify(obj));
}

function BackPanel()
{
	$('#edit').hide();
	$('#panel').show();
	return;
}

function Change(steam)
{
	var obj = { Steam: steam }
	var buf = $('#edit');
	$('#panel').hide();
	document.getElementById("options").innerHTML = "";
	buf.find('table').append("<tr class=\"heading\"><th>Reserved Slot Setting</th><th>Priority Setting</th><th>Kick or Ban</th></tr><tr><td><button class=\"button\" onclick=ChangeReserved('" + steam + "','" + 0 + "')>Public</button><button class=\"button\" onclick=ChangeReserved('" + steam + "','" + 1 + "')>Reserved 1</button><button class=\"button\" onclick=ChangeReserved('" + steam + "','" + 2 + "')>Reserved 2</button><button class=\"button\" onclick=ChangeReserved('" + steam + "','" + 3 + "')>Reserved 3</button></td><td><button class=\"button\" onclick=ChangePriority('" + steam + "','True')>Add</button><button class=\"button\" onclick=ChangePriority('" + steam + "','False')>Remove</button></td><td><button class=\"button\" onclick=BanUser('" + steam + "')>Ban User</button><button class=\"button\" onclick=KickUser('" + steam + "')>Kick User</button></td></tr>");
	$('#edit').show();
	return;
}

function BanUser(steam)
{
	var obj = { Steam: steam }
	$.post('http://fivemqueue/BanUser', JSON.stringify(obj));
	ClosePanel();
	return;
}

function KickUser(steam)
{
	var obj = { Steam: steam }
	$.post('http://fivemqueue/KickUser', JSON.stringify(obj));
	ClosePanel();
	return;
}

function ChangePriority(steam, value)
{
	var obj = { Steam: steam , Value: value}
	$.post('http://fivemqueue/ChangePriority', JSON.stringify(obj));
	ClosePanel();
	return;
}

function ChangeReserved(steam, value)
{
	var obj = { Steam: steam , Value: value}
	$.post('http://fivemqueue/ChangeReserved', JSON.stringify(obj));
	ClosePanel();
	return;
}

$(function()
{
    window.addEventListener('message', function(event)
    {
        var item = event.data;
        var buf = $('#panel');
        if (item.panel && item.panel == 'close')
        {
            document.getElementById("list").innerHTML = "";
            $('#panel').hide();
			$('#edit').hide();
            return;
        }
		if (item.sessionlist)
		{
			var table = buf.find('table');
			table.append("<tr class=\"heading\"><th>Handle</th><th>License</th><th>Steam</th><th>Name</th><th>Reserved</th><th>Reserved Used</th><th>Priority</th><th>State</th><th>Options</th></tr>");
			table.append(item.sessionlist);
			$('#panel').show();
			return;
		}
    }, false);
	
	$(document).keyup(function(e)
    {
        if (e.keyCode == 27 || e.keyCode == 8) // Esc
		{
            if ($('#edit').css('display') == 'block')
			{
				BackPanel();
				return;
			}
			ClosePanel();
			return;
        }
    });
});