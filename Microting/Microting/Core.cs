﻿/*
The MIT License (MIT)

Copyright (c) 2014 microting

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using eFormCommunicator;
using eFormRequest;
using eFormResponse;
using eFormSubscriber;
using eFormSqlController;
using Trools;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Net;
using System.Security.Cryptography;

namespace Microting
{
    class Core : ICore
    {
        #region events
        public event EventHandler HandleCaseCreated;
        public event EventHandler HandleCaseRetrived;
        public event EventHandler HandleCaseUpdated;
        public event EventHandler HandleCaseDeleted;
        public event EventHandler HandleFileDownloaded;
        public event EventHandler HandleSiteActivated;

        public event EventHandler HandleEventLog;
        public event EventHandler HandleEventMessage;
        public event EventHandler HandleEventWarning;
        public event EventHandler HandleEventException;
        #endregion

        #region var
        Communicator communicator;
        SqlController sqlController;
        Subscriber subscriber;
        Tools t = new Tools();

        object _lockMain = new object();
        object _lockEventClient = new object();
        object _lockEventServer = new object();
        bool updateIsRunningFiles = true;
        bool updateIsRunningNotifications = true;
        bool coreRunning = false;
        bool coreRestarting = false;
        bool coreStatChanging = false;

        string comToken;
        string comAddress;
        string subscriberToken;
        string subscriberAddress;
        string subscriberName;
        string serverConnectionString;
        int userId;
        string fileLocation;
        bool logEvents;
        #endregion

        #region con
        public Core(string comToken, string comAddress, string subscriberToken, string subscriberAddress, string subscriberName, string serverConnectionString, int userId, string fileLocation, bool logEvents)
        {
            this.comToken = comToken;
            this.comAddress = comAddress;
            this.subscriberToken = subscriberToken;
            this.subscriberAddress = subscriberAddress;
            this.subscriberName = subscriberName;
            this.serverConnectionString = serverConnectionString;
            this.userId = userId;
            this.fileLocation = fileLocation;
            this.logEvents = logEvents;
        }
        #endregion

        #region public
        public void             Start()
        {
            try
            {
                if (!coreRunning && !coreStatChanging)
                {
                    coreStatChanging = true;

                    TriggerLog("");
                    TriggerLog("");
                    TriggerLog("###################################################################################################################");
                    TriggerMessage("Core.Start() at:" + DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToLongTimeString());
                    TriggerLog("###################################################################################################################");
                    TriggerLog("comToken:" + comToken + " comAddress: " + comAddress + " subscriberToken:" + subscriberToken + " subscriberAddress:" + subscriberAddress +
                        " subscriberName:" + subscriberName + " serverConnectionString:" + serverConnectionString + " userId:" + userId + " fileLocation:" + fileLocation);
                    TriggerLog("Controller started");


                    //sqlController
                    sqlController = new SqlController(serverConnectionString, userId);
                    TriggerLog("SqlEformController started");


                    //communicators
                    communicator = new Communicator(comToken, comAddress);
                    TriggerLog("Communicator started");


                    //subscriber
                    subscriber = new Subscriber(subscriberToken, subscriberAddress, subscriberName);
                    TriggerLog("Subscriber created");
                    subscriber.EventMsgClient += CoreHandleEventClient;
                    subscriber.EventMsgServer += CoreHandleEventServer;
                    TriggerLog("Subscriber now triggers events");
                    subscriber.Start();

                    coreRunning = true;
                    coreStatChanging = false;
                }
            }
            catch (Exception ex)
            {
                coreRunning = false;
                coreStatChanging = false;
                throw new Exception("FATAL Exception. Core failed to start", ex);
            }
        }

        public void             Close()
        {
            try
            {
                if (coreRunning && !coreStatChanging)
                {
                    lock (_lockMain) //Will let sending Cases sending finish, before closing
                    {
                        coreStatChanging = true;

                        coreRunning = false;
                        TriggerMessage("Core.Close() at:" + DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToLongTimeString());

                        try
                        {
                            TriggerLog("Subscriber requested to close connection");
                            subscriber.Close(true);
                        }
                        catch { }

                        try
                        {
                            subscriber.EventMsgClient -= CoreHandleEventClient;
                            subscriber.EventMsgServer -= CoreHandleEventServer;
                        }
                        catch { }

                        subscriber = null;

                        TriggerLog("Subscriber no longer triggers events");
                        TriggerLog("Controller closed");
                        TriggerLog("");

                        communicator = null;
                        sqlController = null;

                        coreStatChanging = false;
                    }
                }
            }
            catch (Exception ex)
            {
                coreRunning = false;
                coreStatChanging = false;
                throw new Exception("FATAL Exception. Core failed to close", ex);
            }
        }

        public MainElement      TemplatFromXml(string xmlString)
        {
            try
            {
                TriggerLog("XML to transform:");
                TriggerLog(xmlString);

                //XML HACK
                #region xmlString = corrected xml if needed
                xmlString = xmlString.Replace("<Main>", "<Main xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">");
                xmlString = xmlString.Replace("<Element type=", "<Element xsi:type=");
                xmlString = xmlString.Replace("<DataItem type=", "<DataItem xsi:type=");
                xmlString = xmlString.Replace("<DataItemGroup type=", "<DataItemGroup xsi:type=");

                xmlString = xmlString.Replace("\"MultiSelect\">", "\"Multi_Select\">");
                xmlString = xmlString.Replace("\"SingleSelect\">", "\"Multi_Select\">");
                xmlString = xmlString.Replace("FolderName", "CheckListFolderName");

                xmlString = xmlString.Replace("FolderName", "CheckListFolderName");
                xmlString = xmlString.Replace("FolderName", "CheckListFolderName");

                string temp = t.Locate(xmlString, "<DoneButtonDisabled>", "</DoneButtonDisabled>");
                if (temp == "false")
                {
                    xmlString = xmlString.Replace("DoneButtonDisabled", "DoneButtonEnabled");
                    xmlString = xmlString.Replace("<DoneButtonEnabled>false", "<DoneButtonEnabled>true");
                }
                if (temp == "true")
                {
                    xmlString = xmlString.Replace("DoneButtonDisabled", "DoneButtonEnabled");
                    xmlString = xmlString.Replace("<DoneButtonEnabled>true", "<DoneButtonEnabled>false");
                }
                #endregion

                MainElement mainElement = new MainElement();
                mainElement = mainElement.XmlToClass(xmlString);

                TriggerLog("XML after possible corrections:");
                TriggerLog(xmlString);

                //XML HACK
                mainElement.CaseType = "";
                mainElement.PushMessageTitle = "";
                mainElement.PushMessageBody = "";
                if (mainElement.Repeated < 1)
                {
                    TriggerLog("mainElement.Repeated = 1 // enforced");
                    mainElement.Repeated = 1;
                }

                return mainElement;
            }
            catch (Exception ex)
            {
                TriggerHandleExpection("TemplatFromXml failed", ex, false);

                return null;
            }
        }

        public int              TemplatCreate(MainElement mainElement)
        {
            try
            {
                if (coreRunning)
                {
                    int templatId = sqlController.TemplatCreate(mainElement);
                    TriggerLog("Templat id:" + templatId.ToString() + " created in DB");
                    return templatId;
                }
                else
                    throw new Exception("Core is not running");
            }
            catch (Exception ex)
            {
                TriggerHandleExpection("TemplatCreate failed", ex, true);
                throw new Exception("TemplatCreate failed", ex);
            }
        }

        public MainElement      TemplatRead(int templatId)
        {
            try
            {
                if (coreRunning)
                {
                    TriggerLog("Templat id:" + templatId.ToString() + " trying to be read");
                    return sqlController.TemplatRead(templatId);
                }
                else
                    throw new Exception("Core is not running");
            }
            catch (Exception ex)
            {
                TriggerHandleExpection("TemplatRead failed", ex, true);
                throw new Exception("TemplatRead failed", ex);
            }
        }

        public void             CaseCreate(MainElement mainElement, string caseUId, List<int> siteIds, bool reversed)
        {
            try
            {
                if (coreRunning)
                {
                    string siteIdsStr = string.Join(",", siteIds);
                    TriggerLog("caseUId:" + caseUId + " siteIds:" + siteIdsStr + " reversed:" + reversed.ToString() + ", requested to be created");

                    Thread subscriberThread = new Thread(() => CaseCreateMethodThreaded(mainElement, siteIds, caseUId, DateTime.MinValue, "", "", reversed));
                    subscriberThread.Start();
                }
                else
                    throw new Exception("Core is not running");
            }
            catch (Exception ex)
            {
                TriggerHandleExpection("CaseCreate failed", ex, true);
                throw new Exception("CaseCreate failed", ex);
            }
        }

        public void             CaseCreate(MainElement mainElement, string caseUId, List<int> siteIds, bool reversed, DateTime navisionTime, string numberPlate, string roadNumber)
        {
            try
            {
                if (coreRunning)
                {
                    string siteIdsStr = string.Join(",", siteIds);
                    TriggerLog("caseUId:" + caseUId + " siteIds:" + siteIdsStr + " reversed:" + reversed.ToString() + " navisionTime:" + navisionTime.ToString() + " numberPlate:" + numberPlate +
                        " roadNumber:" + roadNumber + ", requested to be created");

                    Thread subscriberThread = new Thread(() => CaseCreateMethodThreaded(mainElement, siteIds, caseUId, navisionTime, numberPlate, roadNumber, reversed));
                    subscriberThread.Start();
                }
                else
                    throw new Exception("Core is not running");
            }
            catch (Exception ex)
            {
                TriggerHandleExpection("CaseCreate failed", ex, true);
                throw new Exception("CaseCreate failed", ex);
            }
        }

        /// <summary>
        /// Tries to retrieve the full case in the DB.
        /// </summary>
        /// <param name="microtingUId">Microting ID of the eForm case</param>
        /// <param name="checkUId">If left empty, "0" or NULL it will try to retrieve the first check. Alternative is stating the Id of the specific check wanted to retrieve</param>
        public ReplyElement     CaseRead(string microtingUId, string checkUId)
        {
            try
            {
                if (coreRunning)
                {
                    TriggerLog("microtingUId:" + microtingUId + " checkUId:" + checkUId.ToString() + ", requested to be read");

                    if (checkUId == "" || checkUId == "0")
                        checkUId = null;

                    cases aCase = sqlController.CaseReadFull(microtingUId, checkUId);
                    #region handling if no match case found
                    if (aCase == null)
                    {
                        HandleEventWarning("No case found with MuuId:'" + microtingUId + "'", EventArgs.Empty);
                        return null;
                    }
                    #endregion
                    int id = aCase.id;
                    TriggerLog("aCase.id:" + aCase.id.ToString() + ", found");

                    ReplyElement replyElement = sqlController.TemplatRead((int)aCase.check_list_id);

                    List<Answer> lstAnswers = new List<Answer>();
                    List<field_values> lstReplies = sqlController.ChecksRead(microtingUId);
                    #region remove replicates from lstReplies. Ex. multiple pictures
                    List<field_values> lstRepliesTemp = new List<field_values>();
                    bool found;

                    foreach (var reply in lstReplies)
                    {
                        found = false;
                        foreach (var tempReply in lstRepliesTemp)
                        {
                            if (reply.field_id == tempReply.field_id)
                            {
                                found = true;
                                break;
                            }
                        }
                        if (found == false)
                            lstRepliesTemp.Add(reply);
                    }

                    lstReplies = lstRepliesTemp;
                    #endregion

                    foreach (field_values reply in lstReplies)
                    {
                        Answer answer = sqlController.ChecksReadAnswer(reply.id);
                        lstAnswers.Add(answer);
                    }
                    TriggerLog("Questons and answers found");

                    //replace DataItem(s) with DataItem(s) Answer
                    ReplaceDataItemsInElementsWithAnswers(replyElement.ElementList, lstAnswers);

                    return replyElement;
                }
                else
                    throw new Exception("Core is not running");
            }
            catch (Exception ex)
            {
                TriggerHandleExpection("CaseRead failed", ex, true);
                throw new Exception("CaseRead failed", ex);
            }
        }

        public ReplyElement     CaseReadAllSites(string caseUId)
        {
            try
            {
                if (coreRunning)
                {
                    TriggerLog("caseUId:" + caseUId + ", requested to be read");

                    ReplyElement replyElement;

                    string mUID = sqlController.CaseFindActive(caseUId);
                    TriggerLog("mUID:" + mUID + ", found");

                    if (mUID == "")
                        return null;

                    replyElement = CaseRead(mUID, null);

                    return replyElement;
                }
                else
                    throw new Exception("Core is not running");
            }
            catch (Exception ex)
            {
                TriggerHandleExpection("CaseReadAllSites failed", ex, true);
                throw new Exception("CaseReadAllSites failed", ex);
            }
        }

        public bool             CaseDelete(string microtingUId)
        {
            try
            {
                if (coreRunning)
                {
                    TriggerLog("microtingUId:" + microtingUId + ", requested to be deleted");

                    int siteId = -1;

                    var lst = sqlController.CaseFindMatchs(microtingUId);
                    if (lst.Count < 1)
                    {
                        TriggerLog("No matching siteId found");
                        return false;
                    }

                    foreach (var item in lst)
                    {
                        if (item.MicrotingUId == microtingUId)
                            siteId = item.SiteId;
                    }
                    TriggerLog("siteId:" + siteId.ToString() + ", found to match case");

                    Response resp = new Response();
                    string xmlResponse = communicator.Delete(microtingUId, siteId);
                    TriggerLog("XML response:");
                    TriggerLog(xmlResponse);

                    resp = resp.XmlToClass(xmlResponse);
                    if (resp.Value == "success")
                    {
                        sqlController.CaseDelete(microtingUId);
                        return true;
                    }
                    return false;
                }
                else
                    throw new Exception("Core is not running");
            }
            catch (Exception ex)
            {
                TriggerHandleExpection("CaseDelete failed", ex, true);
                throw new Exception("CaseDelete failed", ex);
            }
        }

        public int              CaseDeleteAllSites(string caseUId)
        {
            try
            {
                if (coreRunning)
                {
                    TriggerLog("caseUId:" + caseUId + ", requested to be deleted");
                    int deleted = 0;

                    List<Case_Dto> lst = sqlController.CaseReadByCaseUId(caseUId);
                    TriggerLog("lst.Count:" + lst.Count + ", found");

                    foreach (var item in lst)
                    {
                        Response resp = new Response();
                        resp = resp.XmlToClass(communicator.Delete(item.MicrotingUId, item.SiteId));

                        if (resp.Value == "success")
                            deleted++;
                    }
                    TriggerLog("deleted:" + deleted.ToString() + ", deleted");

                    return deleted;
                }
                else
                    throw new Exception("Core is not running");
            }
            catch (Exception ex)
            {
                TriggerHandleExpection("CaseDeleteAllSites failed", ex, true);
                throw new Exception("CaseDeleteAllSites failed", ex);
            }
        }

        public Case_Dto         CaseLookup(string microtingUId)
        {
            try
            {
                if (coreRunning)
                {
                    TriggerLog("microtingUId:" + microtingUId + ", requested to be looked up");
                    return sqlController.CaseReadByMUId(microtingUId);
                }
                else
                    throw new Exception("Core is not running");
            }
            catch (Exception ex)
            {
                TriggerHandleExpection("CaseLookup failed", ex, true);
                throw new Exception("CaseLookup failed", ex);
            }
        }

        public List<Case_Dto>   CaseLookupAllSites(string caseUId)
        {
            try
            {
                if (coreRunning)
                {
                    TriggerLog("caseUId:" + caseUId + ", requested to be looked up");
                    return sqlController.CaseReadByCaseUId(caseUId);
                }
                else
                    throw new Exception("Core is not running");
            }
            catch (Exception ex)
            {
                TriggerHandleExpection("CaseLookupAllSites failed", ex, true);
                throw new Exception("CaseLookupAllSites failed", ex);
            }
        }

        public bool             CoreRunning()
        {
            return coreRunning;
        }
        #endregion

        #region private
        private void    CaseCreateMethodThreaded(MainElement mainElement, List<int> siteIds, string caseUId, DateTime navisionTime, string numberPlate, string roadNumber, bool reversed)
        {
            try //Threaded method, hence the try/catch
            {
                lock (_lockMain)
                {
                    if (mainElement.Repeated != 1 && reversed == false)
                        throw new ArgumentException("mainElement.Repeat was not equal to 1 & reversed is false. Hence no case can be created");

                    //sending and getting a reply
                    bool found = false;
                    foreach (int siteId in siteIds)
                    {
                        string mUId = SendXml(mainElement, siteId);

                        if (reversed == false)
                            sqlController.CaseCreate(mUId, mainElement.Id, siteId, caseUId, navisionTime, numberPlate, roadNumber);
                        else
                            sqlController.CheckListSitesCreate(mainElement.Id, siteId, mUId);

                        Case_Dto cDto = sqlController.CaseReadByMUId(mUId);
                        HandleCaseCreated(cDto, EventArgs.Empty);
                        TriggerMessage(cDto.ToString() + " has been created");

                        found = true;
                    }

                    if (!found)
                        throw new Exception("CaseCreateFullMethod failed. No matching sites found. No eForms created");

                    TriggerMessage("eForm created");
                }
            }
            catch (Exception ex)
            {
                TriggerHandleExpection("CaseCreateMethodThreaded failed", ex, true);
            }
        }

        private void    ReplaceDataItemsInElementsWithAnswers(List<Element> elementList, List<Answer> lstAnswers)
        {
            foreach (Element element in elementList)
            {
                #region DataElement
                if (element.GetType() == typeof(DataElement))
                {
                    DataElement dataE = (DataElement)element;

                    //replace DataItemGroups
                    foreach (var dataItemGroup in dataE.DataItemGroupList)
                    {
                        FieldGroup fG = (FieldGroup)dataItemGroup;

                        List<eFormRequest.DataItem> dataItemListTemp = new List<eFormRequest.DataItem>();
                        foreach (var dataItem in fG.DataItemList)
                        {
                            foreach (var answer in lstAnswers)
                            {
                                if (dataItem.Id == answer.Id)
                                {
                                    dataItemListTemp.Add(answer);
                                    break;
                                }
                            }
                        }
                        fG.DataItemList = dataItemListTemp;
                    }

                    //replace DataItems
                    List<eFormRequest.DataItem> dataItemListTemp2 = new List<eFormRequest.DataItem>();
                    foreach (var dataItem in dataE.DataItemList)
                    {
                        foreach (var answer in lstAnswers)
                        {
                            if (dataItem.Id == answer.Id)
                            {
                                dataItemListTemp2.Add(answer);
                                break;
                            }
                        }
                    }
                    dataE.DataItemList = dataItemListTemp2;
                }
                #endregion

                #region GroupElement
                if (element.GetType() == typeof(GroupElement))
                {
                    GroupElement groupE = (GroupElement)element;

                    ReplaceDataItemsInElementsWithAnswers(groupE.ElementList, lstAnswers);
                }
                #endregion
            }
        }

        private string  SendXml(MainElement mainElement, int siteId)
        {
            TriggerLog("siteId:" + siteId + ", requested sent eForm");
            string reply = communicator.PostXml(mainElement.ClassToXml(), siteId);

            Response response = new Response();
            response = response.XmlToClass(reply);

            //if reply is "success", it's created
            if (response.Type.ToString().ToLower() == "success")
            {
                return response.Value;
            }

            throw new Exception("siteId:'" + siteId + "' // failed to create eForm at Microting // Response :" + reply);
        }

        #region inward Event handlers
        private void    CoreHandleUpdateDatabases()
        {
            try
            {
                TriggerLog("CoreHandleUpdateDatabases() initiated");

                Thread updateFilesThread = new Thread(() => CoreHandleUpdateFiles());
                updateFilesThread.Start();

                Thread updateNotificationsThread = new Thread(() => CoreHandleUpdateNotifications());
                updateNotificationsThread.Start();
            }
            catch (Exception ex)
            {
                TriggerHandleExpection("CoreHandleUpdateDatabases failed", ex, true);
            }
        }

        private void    CoreHandleUpdateFiles()
        {
            try
            {
                if (updateIsRunningFiles)
                {
                    updateIsRunningFiles = false;

                    #region update files
                    string urlStr = "";
                    bool oneFound = true;
                    while (oneFound)
                    {
                        urlStr = sqlController.FileRead();
                        if (urlStr == "")
                        {
                            oneFound = false;
                            break;
                        }

                        #region finding file name and creating folder if needed
                        FileInfo file = new FileInfo(fileLocation);
                        file.Directory.Create(); // If the directory already exists, this method does nothing.

                        int index = urlStr.LastIndexOf("/") + 1;
                        string fileName = urlStr.Remove(0, index);
                        #endregion

                        #region download file
                        using (var client = new WebClient())
                        {
                            client.DownloadFile(urlStr, fileLocation + fileName);
                        }
                        #endregion

                        #region finding checkSum
                        string chechSum = "";
                        using (var md5 = MD5.Create())
                        {
                            using (var stream = File.OpenRead(fileLocation + fileName))
                            {
                                byte[] grr = md5.ComputeHash(stream);
                                chechSum = BitConverter.ToString(grr).Replace("-","").ToLower();
                            }
                        }
                        #endregion

                        #region checks checkSum
                        if (chechSum != fileName.Substring(0, 32))
                            HandleEventWarning("Download of '" + urlStr + "' failed. Check sum did not match", EventArgs.Empty);
                        #endregion
                        
                        File_Dto fDto = new File_Dto(sqlController.FileCaseFindMUId(urlStr), fileLocation + fileName);
                        HandleFileDownloaded(fDto, EventArgs.Empty);
                        TriggerMessage("Downloaded file '" + urlStr + "'.");

                        sqlController.FileProcessed(urlStr, chechSum, fileLocation, fileName);
                    }
                    #endregion

                    updateIsRunningFiles = true;
                }
            }
            catch (Exception ex)
            {
                TriggerHandleExpection("CoreHandleUpdateFiles failed", ex, true);
            }
        }

        private void    CoreHandleUpdateNotifications()
        {
            try
            {
                if (updateIsRunningNotifications)
                {
                    updateIsRunningNotifications = false;
                
                    #region update notifications
                    string notificationStr, noteMuuId, noteType = "";
                    bool oneFound = true;
                    while (oneFound)
                    {
                        notificationStr = sqlController.NotificationRead();
                        #region if no new notification found - stops method
                        if (notificationStr == "")
                        {
                            oneFound = false;
                            break;
                        }
                        #endregion

                        noteMuuId = t.Locate(notificationStr, "microting_uuid\\\":\\\"", "\\");
                        noteType = t.Locate(notificationStr, "text\\\":\\\"", "\\\"");

                        switch (noteType)
                        {
                            #region check_status / checklist updated by tablet
                            case "check_status":
                                {
                                    List<Case_Dto> caseLst = sqlController.CaseFindMatchs(noteMuuId);

                                    MainElement mainElement = new MainElement();
                                    foreach (Case_Dto aCase in caseLst)
                                    {
                                        if (aCase.SiteId == sqlController.CaseReadByMUId(noteMuuId).SiteId)
                                        {
                                            #region get response's data and update DB with data
                                            string lastId = sqlController.CaseReadCheckIdByMUId(noteMuuId);
                                            string respXml;

                                            if (lastId == null)
                                                respXml = communicator.Retrieve      (noteMuuId, aCase.SiteId);
                                            else
                                                respXml = communicator.RetrieveFromId(noteMuuId, aCase.SiteId, lastId);

                                            Response resp = new Response();
                                            resp = resp.XmlToClass(respXml);

                                            if (resp.Type == Response.ResponseTypes.Success)
                                            {
                                                if (resp.Checks.Count > 0)
                                                {
                                                    sqlController.ChecksCreate(resp, respXml);

                                                    if (lastId == null)
                                                        sqlController.CaseUpdate(noteMuuId, DateTime.Parse(resp.Checks[0].Date), int.Parse(resp.Checks[0].WorkerId), null, int.Parse(resp.Checks[0].UnitId));
                                                    else
                                                        sqlController.CaseUpdate(noteMuuId, DateTime.Parse(resp.Checks[0].Date), int.Parse(resp.Checks[0].WorkerId), resp.Checks[0].Id, int.Parse(resp.Checks[0].UnitId));

                                                    Case_Dto cDto = sqlController.CaseReadByMUId(noteMuuId);
                                                    HandleCaseUpdated(cDto, EventArgs.Empty);
                                                    TriggerMessage(cDto.ToString() + " has been completed");
                                                }
                                            }
                                            else
                                                throw new Exception("Failed to retrive eForm " + noteMuuId + " from site " + aCase.SiteId);
                                            #endregion
                                        }
                                        else
                                        {
                                            #region delete eForm on other tablets and update DB to "deleted"

                                            string respXml = communicator.Delete(aCase.MicrotingUId, aCase.SiteId);
                                            Response resp = new Response();
                                            resp = resp.XmlToClass(respXml);

                                            if (resp.Type == Response.ResponseTypes.Success)
                                                sqlController.CaseDelete(aCase.MicrotingUId);
                                            else
                                                throw new Exception("Failed to delete eForm " + aCase.MicrotingUId + " from site " + aCase.SiteId);

                                            Case_Dto cDto = sqlController.CaseReadByMUId(aCase.MicrotingUId);
                                            HandleCaseDeleted(cDto, EventArgs.Empty);
                                            TriggerMessage(cDto.ToString() + " has been removed");

                                            #endregion
                                        }
                                    }

                                    sqlController.NotificationProcessed(notificationStr);
                                    break;
                                }
                            #endregion

                            #region unit fetch / checklist retrieve by tablet
                            case "unit_fetch":
                                {
                                    Case_Dto cDto = sqlController.CaseReadByMUId(noteMuuId);
                                    HandleCaseRetrived(cDto, EventArgs.Empty);
                                    TriggerMessage(cDto.ToString() + " has been retrived");

                                    sqlController.NotificationProcessed(notificationStr);
                                    break;
                                }
                            #endregion

                            #region unit_activate / tablet added
                            case "unit_activate":
                                {
                                    HandleSiteActivated(noteMuuId, EventArgs.Empty);
                                    TriggerMessage(noteMuuId + " has been added");

                                    sqlController.NotificationProcessed(notificationStr);
                                    break;
                                }
                            #endregion

                            default:
                                throw new IndexOutOfRangeException("Notification type '" + noteType + "' is not known or mapped");
                        }
                    }
                    #endregion

                    updateIsRunningNotifications = true;
                }
            }
            catch (Exception ex)
            {
                TriggerHandleExpection("CoreHandleUpdateNotifications failed", ex, true);
            }
        }

        private void    CoreHandleEventClient(object sender, EventArgs args)
        {
            try
            {
                lock (_lockEventClient)
                {
                    TriggerLog("Client # " + sender.ToString());
                }
            }
            catch (Exception ex)
            {
                TriggerHandleExpection("CoreHandleEventClient failed", ex, false);
            }
        }

        private void    CoreHandleEventServer(object sender, EventArgs args)
        {
            try
            {
                lock (_lockEventServer)
                {
                    string reply = sender.ToString();
                    TriggerLog("Server # " + reply);

                    if (reply.Contains("-update\",\"data"))
                    {
                        if (reply.Contains("\"id\\\":"))
                        {
                            string muuId = t.Locate(reply, "microting_uuid\\\":\\\"", "\\");
                            string nfId = t.Locate(reply, "\"id\\\":", ",");

                            sqlController.NotificationCreate(muuId, reply);
                            subscriber.ConfirmId(nfId);
                        }
                    }

                    //checks if there already are unprocessed files and notifications in the system
                    CoreHandleUpdateDatabases();
                }
            }
            catch (Exception ex)
            {
                TriggerHandleExpection("CoreHandleEventServer failed", ex, true);
            }
        }
        #endregion

        #region outward Event triggers
        private void    TriggerLog(string str)
        {
            if (logEvents)
            {
                HandleEventLog(DateTime.Now.ToLongTimeString() + ":" + str, EventArgs.Empty);
            }
        }

        private void    TriggerMessage(string str)
        {
            TriggerLog(str);
            HandleEventMessage(str, EventArgs.Empty);
        }

        private void    TriggerHandleExpection(string exceptionDescription, Exception ex, bool restartCore)
        {
            try
            {
                //TODO Repeat expection - different result?

                HandleEventException(ex, EventArgs.Empty);

                if (exceptionDescription == null)
                    exceptionDescription = "";

                TriggerMessage("");
                TriggerMessage("######## " + exceptionDescription);
                TriggerMessage("######## EXCEPTION FOUND; BEGIN ########");
                PrintException(ex, 1);
                TriggerMessage("######## EXCEPTION FOUND; ENDED ########");
                TriggerMessage("");

                if (restartCore)
                {
                    Thread coreRestartThread = new Thread(() => RestartCore());
                    coreRestartThread.Start();
                }
            }
            catch
            {
                coreRunning = false;
                throw new Exception("FATAL Exception. Core failed to handle an Expection", ex);
            }
        }

        private void        PrintException(Exception ex, int level)
        {
            if (ex == null)
                return;

            TriggerMessage("######## -Expection at level " + level + "- ########");

            TriggerMessage("Message    :" + ex.Message);
            TriggerMessage("Source     :" + ex.Source);
            TriggerMessage("StackTrace :" + ex.StackTrace);

            PrintException(ex.InnerException, level + 1);
        }

        private void        RestartCore()
        {
            try
            {
                if (coreRestarting == false)
                {
                    coreRestarting = true;

                    Close();
                    TriggerMessage("");
                    TriggerMessage("Trying to restart the Core in 2 seconds");
                    Thread.Sleep(2000);
                    Start();

                    coreRestarting = false;
                }
            }
            catch (Exception ex)
            {
                coreRunning = false;
                throw new Exception("FATAL Exception. Core failed to restart", ex);
            }
        }
        #endregion
        #endregion
    }
}